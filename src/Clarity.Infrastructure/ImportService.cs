using System.Security.Cryptography;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record ImportPreview(CollectionRun Run, IReadOnlyList<SectionResult> Sections,
    IReadOnlyList<ObjectRecord> Objects, IReadOnlyList<RelationshipRecord> Relationships, int NewObjects, int ExistingObjects,
    ReportDiagnostics? Report = null);

public sealed record RawDownload(Stream Content, string ContentType, string FileName);

public sealed class ImportService(
    IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope,
    IArtifactStore store, ZipPackageReader reader, PackageLimits limits, DiscoveryCatalog catalog,
    ReportProfiles? profiles = null)
{
    private readonly ReportProfiles reports = profiles ?? ReportProfiles.Default;
    private ReportLimits ReportLimits => new(limits.CompressedBytes, limits.Lines, ReportLimits.Default.Fields,
        ReportLimits.Default.FieldCharacters);

    public async Task<Guid> UploadAsync(Guid projectId, Guid environmentId, Stream source, string fileName,
        CancellationToken cancellationToken = default) =>
        await StageAsync(projectId, environmentId, source, fileName, null, null, "[]", cancellationToken);

    internal async Task<Guid> StageAsync(Guid projectId, Guid environmentId, Stream source, string fileName,
        Guid? connectionId, Guid? jobId, string continuationJson,
        CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, connectionId is null ? WorkspaceOperation.Import : WorkspaceOperation.Collect, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await EnvironmentAsync(db, project, environmentId, cancellationToken);
        var artifact = await store.SaveAsync(project.WorkspaceId, source, limits.CompressedBytes, cancellationToken);
        var retain = false;
        try
        {
            await using var stored = await store.OpenAsync(project.WorkspaceId, artifact.BlobName, cancellationToken);
            var read = await reader.ReadAsync(stored, artifact.ContentHash, cancellationToken);
            catalog.ValidatePackage(read.Package);
            if (catalog.Producers[read.Package.Manifest.ProducerId].Channel == "Import")
            {
                // Report producers are interpreted only from the raw report through their profile, never from a claimed ZIP.
                throw new ContractException("ReportRequiresProfileUpload", "manifest.json");
            }
            await ValidateContextAsync(db, project, environment, read.Package, cancellationToken);
            if (connectionId is { } connected)
            {
                var connection = await GraphDiscoveryService.ConnectionAsync(db, project, environment, connected, cancellationToken);
                if (read.Package.Manifest.RunId != jobId || read.Package.Manifest.ProducerId != connection.ProducerId
                    || read.Package.Manifest.RequestId is not null) { throw new ConflictException(); }
            }
            var (id, created) = await RecordAsync(db, project, environment, read.Package, artifact, fileName, read.UncompressedBytes,
                catalog.Producers[read.Package.Manifest.ProducerId].Channel == "Graph" ? CollectionChannel.Graph : CollectionChannel.PowerShell,
                connectionId, jobId, continuationJson, cancellationToken);
            retain = created;
            return id;
        }
        finally
        {
            if (!retain) { await store.DeleteAsync(project.WorkspaceId, artifact.BlobName, CancellationToken.None); }
        }
    }

    /// <summary>
    /// Stages a consultant-uploaded administrative report interpreted by a supported, versioned profile. The tenant scope is
    /// bound to the authorized environment and recorded as consultant-supplied; nothing is published before confirmation.
    /// </summary>
    public async Task<Guid> UploadReportAsync(Guid projectId, Guid environmentId, string profileId, string profileVersion,
        CollectionScope requestedScope, bool scopeAttested, Stream source, string fileName, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Import, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await EnvironmentAsync(db, project, environmentId, cancellationToken);
        var profile = reports.Require(profileId, profileVersion);
        catalog.RequireProducer(profile.ProducerId);
        if (environment.Kind.ToString() != profile.EnvironmentKind || !Guid.TryParse(environment.ExternalId, out _))
        {
            throw new ContractException("ReportEnvironmentUnsupported", "profile");
        }
        if (!scopeAttested) { throw new ContractException("ReportScopeNotAttested", "profile"); }
        if (requestedScope.Kind != profile.ScopeKind
            || !string.Equals(requestedScope.Value?.Trim(), environment.ExternalId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ContractException("ReportScopeMismatch", "profile");
        }
        StoredArtifact artifact;
        try
        {
            artifact = await store.SaveAsync(project.WorkspaceId, source, limits.CompressedBytes, cancellationToken);
        }
        catch (ContractException error) when (error.Code is "CompressedSizeLimit" or "SizeLimit")
        {
            throw new ContractException("ReportFileTooLarge", CsvReportReader.FileName);
        }
        var retain = false;
        try
        {
            var context = new ReportContext(environment.ExternalId, new CollectionScope(profile.ScopeKind, environment.ExternalId),
                artifact.ContentHash);
            var conversion = await ConvertReportAsync(project.WorkspaceId, artifact.BlobName, artifact.ContentHash, profile, context,
                cancellationToken);
            catalog.ValidatePackage(conversion.Package);
            await ValidateContextAsync(db, project, environment, conversion.Package, cancellationToken);
            await ValidateReportEvidenceAsync(db, project, environment, conversion.Diagnostics, cancellationToken);
            var (id, created) = await RecordAsync(db, project, environment, conversion.Package, artifact, fileName, artifact.Bytes,
                CollectionChannel.Report, null, null, "[]", cancellationToken);
            retain = created;
            return id;
        }
        finally
        {
            if (!retain) { await store.DeleteAsync(project.WorkspaceId, artifact.BlobName, CancellationToken.None); }
        }
    }

    private async Task<(Guid Id, bool Created)> RecordAsync(ClarityDbContext db, Project project, ProjectEnvironment environment,
        DiscoveryPackage package, StoredArtifact artifact, string fileName, long uncompressedBytes, CollectionChannel channel,
        Guid? connectionId, Guid? jobId, string continuationJson, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PublicationService.LockEnvironmentAsync(db, project.WorkspaceId, environment.Id, cancellationToken);
        var manifest = package.Manifest;
        var existingRun = await db.CollectionRuns.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == project.Id && x.EnvironmentId == environment.Id && x.ExternalRunId == manifest.RunId, cancellationToken);
        if (existingRun is not null)
        {
            return existingRun.ContentHash == artifact.ContentHash ? (existingRun.Id, false) : throw new ConflictException();
        }
        var scopeHash = DiscoveryScope.Hash(manifest.Scope);
        var existingContent = await db.CollectionRuns.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == project.Id && x.EnvironmentId == environment.Id && x.ScopeHash == scopeHash
            && x.ContentHash == artifact.ContentHash, cancellationToken);
        if (existingContent is not null)
        {
            // The same bytes interpreted by another producer or profile version are a conflict, not a silent reuse.
            return existingContent.ProducerId == manifest.ProducerId && existingContent.ProducerVersion == manifest.ProducerVersion
                ? (existingContent.Id, false) : throw new ConflictException();
        }
        CollectionRequest? request = null;
        if (manifest.RequestId is { } requestId)
        {
            request = await db.CollectionRequests.SingleAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == requestId, cancellationToken);
            if (request.State == CollectionRequestState.Integrated) { throw new ConflictException(); }
            request.State = CollectionRequestState.FileToConfirm;
        }
        var run = new CollectionRun
        {
            Id = jobId ?? Guid.NewGuid(),
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            EnvironmentId = environment.Id,
            ExternalRunId = manifest.RunId,
            RequestId = manifest.RequestId,
            ConnectionId = connectionId,
            Channel = channel,
            ContinuationJson = continuationJson,
            ConfirmedAtUtc = connectionId is null ? null : DateTimeOffset.UtcNow,
            ProducerId = manifest.ProducerId,
            ProducerVersion = manifest.ProducerVersion,
            ScopeJson = DiscoveryScope.Serialize(manifest.Scope),
            ScopeHash = scopeHash,
            ContentHash = artifact.ContentHash,
            ManifestJson = JsonSerializer.Serialize(manifest, ContractJson.Options),
            StartedAtUtc = manifest.StartedAtUtc,
            CompletedAtUtc = manifest.CompletedAtUtc,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        };
        db.CollectionRuns.Add(run);
        db.SectionRuns.AddRange(package.Results.Select(result => new SectionRun
        {
            WorkspaceId = project.WorkspaceId,
            RunId = run.Id,
            SectionId = result.SectionId,
            CapabilityId = result.CapabilityId,
            ScopeJson = DiscoveryScope.Serialize(result.Scope),
            ScopeHash = DiscoveryScope.Hash(result.Scope),
            Coverage = result.Status,
            ObjectCount = result.ObjectCount,
            RelationshipCount = result.RelationshipCount,
            ResultJson = JsonSerializer.Serialize(result, ContractJson.Options)
        }));
        var name = Path.GetFileName(fileName);
        db.RawArtifacts.Add(new RawArtifact
        {
            WorkspaceId = project.WorkspaceId,
            RunId = run.Id,
            BlobName = artifact.BlobName,
            ContentHash = artifact.ContentHash,
            FileName = name[..Math.Min(name.Length, 200)],
            CompressedBytes = artifact.Bytes,
            UncompressedBytes = uncompressedBytes
        });
        Audit(db, project.WorkspaceId, run.Id, "CollectionUploaded");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (run.Id, true);
    }

    public async Task<ImportPreview> PreviewAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var run = await RunAsync(db, project, runId, cancellationToken);
        var report = run.Channel == CollectionChannel.Report ? await ReadReportAsync(db, run, cancellationToken) : null;
        var package = report?.Package ?? await ReadAsync(db, run, cancellationToken);
        var existing = 0;
        foreach (var group in package.Objects.DistinctBy(x => (x.Namespace, x.Type, x.ExternalId)).GroupBy(x => (x.Namespace, x.Type)))
        {
            foreach (var chunk in group.Chunk(500))
            {
                var ids = chunk.Select(x => x.ExternalId).ToArray();
                existing += await db.ExternalKeys.CountAsync(x => x.IsPrimary && x.WorkspaceId == project.WorkspaceId && x.EnvironmentId == run.EnvironmentId
                    && x.Namespace == group.Key.Namespace && x.Type == group.Key.Type && ids.Contains(x.ExternalId), cancellationToken);
            }
        }
        var total = package.Objects.DistinctBy(x => (x.Namespace, x.Type, x.ExternalId)).Count();
        Audit(db, project.WorkspaceId, run.Id, "CollectionPreviewed");
        await db.SaveChangesAsync(cancellationToken);
        return new ImportPreview(run, package.Results, package.Objects.Take(20).ToArray(),
            package.Relationships.Take(20).ToArray(), total - existing, existing, report?.Diagnostics);
    }

    public async Task ConfirmAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Import, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var run = await RunAsync(db, project, runId, cancellationToken);
        var environment = await EnvironmentAsync(db, project, run.EnvironmentId, cancellationToken);
        await ReadForPublicationAsync(db, project, environment, run, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PublicationService.LockEnvironmentAsync(db, project.WorkspaceId, environment.Id, cancellationToken);
        await db.Entry(run).ReloadAsync(cancellationToken);
        if (await db.CollectionJobs.AnyAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == run.Id, cancellationToken)) { return; }
        run.ConfirmedAtUtc = DateTimeOffset.UtcNow;
        db.CollectionJobs.Add(new CollectionJob
        {
            Id = run.Id,
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            Kind = CollectionJobKind.Import,
            PayloadJson = JsonSerializer.Serialize(new ImportJobPayload(run.Id), ContractJson.Options),
            ConcurrencyKey = DiscoveryScope.Hash(new CollectionScope("resource", $"{project.WorkspaceId:N}:{environment.Id:N}")),
            CorrelationId = run.CorrelationId,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId
        });
        Audit(db, project.WorkspaceId, run.Id, "CollectionConfirmed");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Stream> DownloadAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default) =>
        (await DownloadRawAsync(projectId, runId, cancellationToken)).Content;

    /// <summary>Returns the retained raw proof with a content type and a sanitized name; never the private Blob path.</summary>
    public async Task<RawDownload> DownloadRawAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var run = await RunAsync(db, project, runId, cancellationToken);
        var artifact = await db.RawArtifacts.SingleAsync(x => x.WorkspaceId == project.WorkspaceId && x.RunId == run.Id, cancellationToken);
        Audit(db, project.WorkspaceId, run.Id, "RawArtifactDownloaded");
        await db.SaveChangesAsync(cancellationToken);
        var content = await store.OpenAsync(project.WorkspaceId, artifact.BlobName, cancellationToken);
        return run.Channel == CollectionChannel.Report
            ? new RawDownload(content, "text/csv", RawFileName(artifact.FileName, ".csv", "report.csv"))
            : new RawDownload(content, "application/zip", "result.zip");
    }

    internal async Task<DiscoveryPackage> ReadAsync(ClarityDbContext db, CollectionRun run, CancellationToken cancellationToken)
    {
        if (run.Channel == CollectionChannel.Report) { return (await ReadReportAsync(db, run, cancellationToken)).Package; }
        var artifact = await db.RawArtifacts.SingleAsync(x => x.WorkspaceId == run.WorkspaceId && x.RunId == run.Id, cancellationToken);
        await using var source = await store.OpenAsync(run.WorkspaceId, artifact.BlobName, cancellationToken);
        return (await reader.ReadAsync(source, artifact.ContentHash, cancellationToken)).Package;
    }

    /// <summary>
    /// Re-reads a staged run and enforces everything required before a confirmation or a publication job: package and context
    /// validation and, for profiled reports, at least one usable row and no contradiction with known environment evidence.
    /// </summary>
    internal async Task<DiscoveryPackage> ReadForPublicationAsync(ClarityDbContext db, Project project, ProjectEnvironment environment,
        CollectionRun run, CancellationToken cancellationToken)
    {
        if (run.Channel != CollectionChannel.Report)
        {
            var package = await ReadAsync(db, run, cancellationToken);
            catalog.ValidatePackage(package);
            await ValidateContextAsync(db, project, environment, package, cancellationToken);
            return package;
        }
        var conversion = await ReadReportAsync(db, run, cancellationToken);
        catalog.ValidatePackage(conversion.Package);
        await ValidateContextAsync(db, project, environment, conversion.Package, cancellationToken);
        if (conversion.Package.Objects.Count == 0 || conversion.Package.Results.Any(x => x.Status == CoverageStatus.Failed))
        {
            throw new ContractException("ReportNoUsableRows", CsvReportReader.FileName);
        }
        await ValidateReportEvidenceAsync(db, project, environment, conversion.Diagnostics, cancellationToken);
        return conversion.Package;
    }

    /// <summary>Re-interprets a staged report deterministically from its retained raw file and recorded profile/scope.</summary>
    internal async Task<ReportConversion> ReadReportAsync(ClarityDbContext db, CollectionRun run, CancellationToken cancellationToken)
    {
        var artifact = await db.RawArtifacts.SingleAsync(x => x.WorkspaceId == run.WorkspaceId && x.RunId == run.Id, cancellationToken);
        var manifest = JsonSerializer.Deserialize<PackageManifest>(run.ManifestJson, ContractJson.Options)
            ?? throw new ContractException("ReportProfileUnavailable", "profile");
        var profile = reports.ForProducer(run.ProducerId, run.ProducerVersion)
            ?? throw new ContractException("ReportProfileUnavailable", "profile");
        var context = new ReportContext(manifest.EnvironmentExternalId, manifest.Scope, artifact.ContentHash,
            manifest.ModuleVersions.TryGetValue("scopeSource", out var source) && source == "observed", manifest.SourceId);
        var conversion = await ConvertReportAsync(run.WorkspaceId, artifact.BlobName, artifact.ContentHash, profile, context,
            cancellationToken);
        if (conversion.Package.Manifest.RunId != run.ExternalRunId) { throw new ConflictException(); }
        return conversion;
    }

    private async Task<ReportConversion> ConvertReportAsync(Guid workspaceId, string blobName, string contentHash, ReportProfile profile,
        ReportContext context, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        try
        {
            await using var stored = await store.OpenAsync(workspaceId, blobName, cancellationToken);
            await using var bounded = new LimitedReadStream(stored, limits.CompressedBytes, leaveOpen: true);
            await bounded.CopyToAsync(buffer, cancellationToken);
        }
        catch (ContractException error) when (error.Code == "SizeLimit")
        {
            throw new ContractException("ReportFileTooLarge", CsvReportReader.FileName);
        }
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), contentHash, StringComparison.Ordinal))
        {
            throw new ContractException("ContentHashMismatch", CsvReportReader.FileName);
        }
        var table = CsvReportReader.Read(bytes, ReportLimits);
        reports.RequireBestMatch(profile, table.Headers);
        return ReportAdapter.Convert(table, profile, context);
    }

    /// <summary>
    /// Blocks a report whose own content contradicts what the authorized environment already proved: user principal domains
    /// outside the tenant's collected verified domains, or a SharePoint root different from previously imported site URLs.
    /// Absence of prior evidence is not treated as confirmation; the scope stays recorded as consultant-supplied.
    /// </summary>
    internal static async Task ValidateReportEvidenceAsync(ClarityDbContext db, Project project, ProjectEnvironment environment,
        ReportDiagnostics diagnostics, CancellationToken cancellationToken)
    {
        if (diagnostics.UpnDomains.Count != 0)
        {
            var known = await db.ExternalKeys.AsNoTracking().Where(x => x.WorkspaceId == project.WorkspaceId
                    && x.EnvironmentId == environment.Id && x.IsPrimary && x.Type == "domain" && x.Namespace == "microsoft.graph")
                .Select(x => x.ExternalId).Take(5000).ToListAsync(cancellationToken);
            var domains = known.Select(x => x.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            var unknown = diagnostics.UpnDomains.Where(x => !domains.Contains(x)).ToArray();
            if (domains.Count != 0 && unknown.Length != 0)
            {
                throw new ContractException("ReportEnvironmentConflict", CsvReportReader.FileName, null, unknown.Take(10).ToArray());
            }
        }
        if (diagnostics.SiteRoots.Count == 1)
        {
            var root = diagnostics.SiteRoots[0];
            var dot = root.IndexOf('.', StringComparison.Ordinal);
            var personal = $"https://{root[..dot]}-my{root[dot..]}";
            var site = $"https://{root}";
            var conflicting = await db.AlternateKeyPeriods.AsNoTracking().Where(x => x.WorkspaceId == project.WorkspaceId
                    && x.EnvironmentId == environment.Id && x.Namespace == "url" && x.NormalizedValue.StartsWith("https://")
                    && x.NormalizedValue != site && !x.NormalizedValue.StartsWith(site + "/")
                    && x.NormalizedValue != personal && !x.NormalizedValue.StartsWith(personal + "/"))
                .Select(x => x.NormalizedValue).Take(50).ToListAsync(cancellationToken);
            var roots = conflicting.Select(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) ? ReportAdapter.Root(uri.Host) : null)
                .OfType<string>().Where(x => x != root).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(9).ToArray();
            if (roots.Length != 0)
            {
                throw new ContractException("ReportEnvironmentConflict", CsvReportReader.FileName, null, [root, .. roots]);
            }
        }
    }

    private static string RawFileName(string name, string extension, string fallback)
    {
        var clean = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').ToArray()).Trim('.');
        return clean.Length != 0 && clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? clean : fallback;
    }

    internal static async Task<CollectionRun> RunAsync(ClarityDbContext db, Project project, Guid runId, CancellationToken cancellationToken) =>
        await db.CollectionRuns.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == project.Id && x.Id == runId,
            cancellationToken) ?? throw new AccessDeniedException();

    internal static async Task<ProjectEnvironment> EnvironmentAsync(ClarityDbContext db, Project project, Guid id, CancellationToken cancellationToken) =>
        await db.Environments.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == project.Id
            && x.Id == id && x.TargetState == TargetState.Existing, cancellationToken) ?? throw new AccessDeniedException();

    internal static async Task ValidateContextAsync(ClarityDbContext db, Project project, ProjectEnvironment environment,
        DiscoveryPackage package, CancellationToken cancellationToken)
    {
        var manifest = package.Manifest;
        if (manifest.EnvironmentKind != environment.Kind.ToString()
            || !string.Equals(manifest.EnvironmentExternalId, environment.ExternalId, StringComparison.OrdinalIgnoreCase)
            || ResourceScriptScope.RequiresRequest(manifest.ProducerId) && (manifest.RequestId is null
                || !ResourceScriptScope.MatchesEvidence(environment.Kind, package))
            || package.Results.Any(x => x.Scope != manifest.Scope))
        {
            throw new ContractException("EnvironmentOrScopeMismatch", "manifest.json");
        }
        if (manifest.RequestId is not { } id) { return; }
        var request = await db.CollectionRequests.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.ProjectId == project.Id && x.EnvironmentId == environment.Id && x.Id == id, cancellationToken);
        if (request is null || request.ScopeHash != DiscoveryScope.Hash(manifest.Scope)
            || request.ProducerId != manifest.ProducerId || request.ScriptVersion != manifest.ProducerVersion
            || !JsonSerializer.Deserialize<string[]>(request.SectionsJson, ContractJson.Options)!.Order(StringComparer.Ordinal)
                .SequenceEqual(package.Results.Select(x => x.CapabilityId).Order(StringComparer.Ordinal)))
        {
            throw new ContractException("RequestMismatch", "manifest.json");
        }
    }

    private void Audit(ClarityDbContext db, Guid workspaceId, Guid resourceId, string action) => db.AuditEvents.Add(new AuditEvent
    {
        WorkspaceId = workspaceId,
        ResourceId = resourceId,
        Action = action,
        ActorTenantId = scope.Identity.TenantId,
        ActorObjectId = scope.Identity.ObjectId,
        CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
    });
}

public sealed record ImportJobPayload(Guid RunId);
