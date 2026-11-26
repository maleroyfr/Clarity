using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record ImportPreview(CollectionRun Run, IReadOnlyList<SectionResult> Sections,
    IReadOnlyList<ObjectRecord> Objects, IReadOnlyList<RelationshipRecord> Relationships, int NewObjects, int ExistingObjects);

public sealed class ImportService(
    IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope,
    IArtifactStore store, ZipPackageReader reader, PackageLimits limits, DiscoveryCatalog catalog)
{
    public async Task<Guid> UploadAsync(Guid projectId, Guid environmentId, Stream source, string fileName,
        CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Import, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var environment = await EnvironmentAsync(db, project, environmentId, cancellationToken);
        var artifact = await store.SaveAsync(project.WorkspaceId, source, limits.CompressedBytes, cancellationToken);
        var retain = false;
        try
        {
            await using var stored = await store.OpenAsync(project.WorkspaceId, artifact.BlobName, cancellationToken);
            var read = await reader.ReadAsync(stored, artifact.ContentHash, cancellationToken);
            catalog.ValidatePackage(read.Package);
            await ValidateContextAsync(db, project, environment, read.Package, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await PublicationService.LockEnvironmentAsync(db, project.WorkspaceId, environment.Id, cancellationToken);
            var manifest = read.Package.Manifest;
            var existingRun = await db.CollectionRuns.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
                && x.ProjectId == project.Id && x.EnvironmentId == environment.Id && x.ExternalRunId == manifest.RunId, cancellationToken);
            if (existingRun is not null)
            {
                return existingRun.ContentHash == artifact.ContentHash ? existingRun.Id : throw new ConflictException();
            }
            var scopeHash = DiscoveryScope.Hash(manifest.Scope);
            var existingContent = await db.CollectionRuns.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
                && x.ProjectId == project.Id && x.EnvironmentId == environment.Id && x.ScopeHash == scopeHash
                && x.ContentHash == artifact.ContentHash, cancellationToken);
            if (existingContent is not null) { return existingContent.Id; }
            CollectionRequest? request = null;
            if (manifest.RequestId is { } requestId)
            {
                request = await db.CollectionRequests.SingleAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == requestId, cancellationToken);
                if (request.State == CollectionRequestState.Integrated) { throw new ConflictException(); }
                request.State = CollectionRequestState.FileToConfirm;
            }
            var run = new CollectionRun
            {
                WorkspaceId = project.WorkspaceId,
                ProjectId = project.Id,
                EnvironmentId = environment.Id,
                ExternalRunId = manifest.RunId,
                RequestId = manifest.RequestId,
                Channel = CollectionChannel.PowerShell,
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
            db.SectionRuns.AddRange(read.Package.Results.Select(result => new SectionRun
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
            db.RawArtifacts.Add(new RawArtifact
            {
                WorkspaceId = project.WorkspaceId,
                RunId = run.Id,
                BlobName = artifact.BlobName,
                ContentHash = artifact.ContentHash,
                FileName = Path.GetFileName(fileName)[..Math.Min(Path.GetFileName(fileName).Length, 200)],
                CompressedBytes = artifact.Bytes,
                UncompressedBytes = read.UncompressedBytes
            });
            Audit(db, project.WorkspaceId, run.Id, "CollectionUploaded");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            retain = true;
            return run.Id;
        }
        finally
        {
            if (!retain) { await store.DeleteAsync(project.WorkspaceId, artifact.BlobName, CancellationToken.None); }
        }
    }

    public async Task<ImportPreview> PreviewAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var run = await RunAsync(db, project, runId, cancellationToken);
        var package = await ReadAsync(db, run, cancellationToken);
        var existing = 0;
        foreach (var group in package.Objects.DistinctBy(x => (x.Namespace, x.Type, x.ExternalId)).GroupBy(x => (x.Namespace, x.Type)))
        {
            foreach (var chunk in group.Chunk(500))
            {
                var ids = chunk.Select(x => x.ExternalId).ToArray();
                existing += await db.ExternalKeys.CountAsync(x => x.WorkspaceId == project.WorkspaceId && x.EnvironmentId == run.EnvironmentId
                    && x.Namespace == group.Key.Namespace && x.Type == group.Key.Type && ids.Contains(x.ExternalId), cancellationToken);
            }
        }
        var total = package.Objects.DistinctBy(x => (x.Namespace, x.Type, x.ExternalId)).Count();
        Audit(db, project.WorkspaceId, run.Id, "CollectionPreviewed");
        await db.SaveChangesAsync(cancellationToken);
        return new ImportPreview(run, package.Results, package.Objects.Take(20).ToArray(),
            package.Relationships.Take(20).ToArray(), total - existing, existing);
    }

    public async Task ConfirmAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Import, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var run = await RunAsync(db, project, runId, cancellationToken);
        var package = await ReadAsync(db, run, cancellationToken);
        catalog.ValidatePackage(package);
        var environment = await EnvironmentAsync(db, project, run.EnvironmentId, cancellationToken);
        await ValidateContextAsync(db, project, environment, package, cancellationToken);
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

    public async Task<Stream> DownloadAsync(Guid projectId, Guid runId, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var run = await RunAsync(db, project, runId, cancellationToken);
        var artifact = await db.RawArtifacts.SingleAsync(x => x.WorkspaceId == project.WorkspaceId && x.RunId == run.Id, cancellationToken);
        Audit(db, project.WorkspaceId, run.Id, "RawArtifactDownloaded");
        await db.SaveChangesAsync(cancellationToken);
        return await store.OpenAsync(project.WorkspaceId, artifact.BlobName, cancellationToken);
    }

    internal async Task<DiscoveryPackage> ReadAsync(ClarityDbContext db, CollectionRun run, CancellationToken cancellationToken)
    {
        var artifact = await db.RawArtifacts.SingleAsync(x => x.WorkspaceId == run.WorkspaceId && x.RunId == run.Id, cancellationToken);
        await using var source = await store.OpenAsync(run.WorkspaceId, artifact.BlobName, cancellationToken);
        return (await reader.ReadAsync(source, artifact.ContentHash, cancellationToken)).Package;
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
