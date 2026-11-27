using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record GraphJobPayload(Guid EnvironmentId, Guid ConnectionId, CollectionScope Scope, string[] Sections);
public sealed record GraphContinuation(string CapabilityId, string ScopeHash, string DeltaLink);

public sealed class GraphJobHandler(IGraphSessionFactory sessions, EntraCollector collector,
    IArtifactStore store, ZipPackageReader reader, PackageLimits limits, DiscoveryCatalog catalog,
    PackageValidator validator, ReportProfiles? profiles = null) : ICollectionJobHandler
{
    private readonly ReportProfiles reports = profiles ?? ReportProfiles.Default;

    public CollectionJobKind Kind => CollectionJobKind.Graph;

    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        var job = execution.Job;
        var payload = JsonSerializer.Deserialize<GraphJobPayload>(job.PayloadJson, ContractJson.Options)
            ?? throw new ContractException("JobPayload", "job");
        var project = await execution.Access.RequireAsync(job.ProjectId, WorkspaceOperation.Collect, cancellationToken);
        var scope = new WorkspaceScope(new UserIdentity(job.ActorTenantId, job.ActorObjectId), [project.WorkspaceId]);
        var imports = new ImportService(execution.Factory, execution.Access, scope, store, reader, limits, catalog, reports);
        CollectionRun? prepared;
        Connection connection;
        string producerId;
        await using (var db = await execution.Factory.CreateDbContextAsync(cancellationToken))
        {
            // Connection-staged runs take the job ID as run ID; downloaded reports keep their profile's deterministic external run ID.
            prepared = await db.CollectionRuns.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
                && x.ProjectId == project.Id && x.EnvironmentId == payload.EnvironmentId && x.Id == job.Id, cancellationToken);
            var environment = await ImportService.EnvironmentAsync(db, project, payload.EnvironmentId, cancellationToken);
            connection = await GraphDiscoveryService.ConnectionAsync(db, project, environment, payload.ConnectionId, cancellationToken);
            producerId = GraphDiscoveryService.ValidateSelection(catalog, connection, payload.Scope, payload.Sections);
        }
        if (prepared is not null) { Check(prepared, connection, producerId); }
        if (prepared is null && catalog.Producers[producerId].Channel == "Import")
        {
            using var session = await sessions.CreateAsync(connection, cancellationToken);
            await session.Transport.VerifyTenantAsync(connection.TenantId, cancellationToken);
            var id = await DownloadReportAsync(execution, imports, session.Transport, payload, producerId, connection, project.Id,
                cancellationToken);
            await using var preparedDb = await execution.Factory.CreateDbContextAsync(cancellationToken);
            prepared = await ImportService.RunAsync(preparedDb, project, id, cancellationToken);
            Check(prepared, connection, producerId);
        }
        if (prepared is null)
        {
            using var session = await sessions.CreateAsync(connection, cancellationToken);
            await session.Transport.VerifyTenantAsync(connection.TenantId, cancellationToken);
            var started = DateTimeOffset.UtcNow;
            var objects = new List<ObjectRecord>();
            var relationships = new List<RelationshipRecord>();
            var results = new List<SectionResult>();
            var continuations = new List<GraphContinuation>();
            var intune = new IntuneCollector(catalog);
            var applicationsSso = new ApplicationsSsoCollector(catalog);
            var sharePoint = new SharePointCollector(catalog);
            var oneDrive = new OneDriveCollector(catalog);
            var teams = new TeamsCollector(catalog);
            foreach (var capability in payload.Sections)
            {
                await execution.ReportProgressAsync(0, cancellationToken);
                await execution.Access.RequireAsync(job.ProjectId, WorkspaceOperation.Collect, cancellationToken);
                string? hash = null;
                string? delta = null;
                if (capability == "entra.users")
                {
                    hash = CursorScopeHash(payload.Scope, catalog);
                    await using var db = await execution.Factory.CreateDbContextAsync(cancellationToken);
                    var oldest = DateTimeOffset.UtcNow.AddDays(-6);
                    delta = await db.GraphCursors.Where(x => x.WorkspaceId == project.WorkspaceId && x.EnvironmentId == payload.EnvironmentId
                        && x.ConnectionId == connection.Id && x.CapabilityId == capability && x.ScopeHash == hash && x.PublishedAtUtc > oldest)
                        .Select(x => x.DeltaLink).SingleOrDefaultAsync(cancellationToken);
                }
                var section = connection.ProducerId switch
                {
                    IntuneCollector.ProducerId => await intune.CollectAsync(session.Transport, capability, payload.Scope, cancellationToken),
                    ApplicationsSsoCollector.ProducerId => await applicationsSso.CollectAsync(session.Transport, capability, payload.Scope, cancellationToken),
                    SharePointCollector.ProducerId => await sharePoint.CollectAsync(session.Transport, capability, payload.Scope, cancellationToken),
                    OneDriveCollector.ProducerId => await oneDrive.CollectAsync(session.Transport, capability, payload.Scope, cancellationToken),
                    TeamsCollector.ProducerId => await teams.CollectAsync(session.Transport, capability, payload.Scope, cancellationToken),
                    _ => await collector.CollectAsync(session.Transport, capability, payload.Scope, delta, cancellationToken)
                };
                objects.AddRange(section.Objects);
                relationships.AddRange(section.Relationships);
                results.Add(section.Result);
                if (section.DeltaLink is not null && hash is not null) { continuations.Add(new(capability, hash, section.DeltaLink)); }
            }
            var producer = catalog.RequireProducer(connection.ProducerId);
            var package = new DiscoveryPackage(new PackageManifest("1.0", job.Id, producer.Id, producer.Version, "M365Tenant",
                connection.TenantId.ToString(), payload.Scope, started, DateTimeOffset.UtcNow,
                new Dictionary<string, string> { ["Microsoft.Graph"] = $"REST {producer.ApiVersion}" }, "Microsoft Graph"), objects, relationships, results);
            using var zip = new MemoryStream();
            await PackageZipWriter.WriteAsync(package, zip, cancellationToken);
            zip.Position = 0;
            await execution.ReportProgressAsync(0, cancellationToken);
            var id = await imports.StageAsync(project.Id, payload.EnvironmentId, zip, "graph-results.zip", connection.Id, job.Id,
                JsonSerializer.Serialize(continuations, ContractJson.Options), cancellationToken);
            await using var preparedDb = await execution.Factory.CreateDbContextAsync(cancellationToken);
            prepared = await ImportService.RunAsync(preparedDb, project, id, cancellationToken);
        }
        var pipeline = new ImportJobHandler(store, reader, limits, catalog, validator, reports);
        await pipeline.PublishAsync(execution, prepared.Id, cancellationToken);
        await execution.ReportProgressAsync(payload.Sections.Length, cancellationToken);
        await execution.Access.RequireAsync(job.ProjectId, WorkspaceOperation.Collect, cancellationToken);
        await using var complete = await execution.Factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await complete.Database.BeginTransactionAsync(cancellationToken);
        await SqlJobQueue.FenceAsync(complete, job, DateTimeOffset.UtcNow, cancellationToken);
        await PublicationService.LockEnvironmentAsync(complete, project.WorkspaceId, payload.EnvironmentId, cancellationToken);
        foreach (var continuation in JsonSerializer.Deserialize<GraphContinuation[]>(prepared.ContinuationJson, ContractJson.Options)!)
        {
            GraphTransport.ValidateUri(continuation.DeltaLink);
            var cursor = await complete.GraphCursors.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
                && x.EnvironmentId == payload.EnvironmentId && x.ConnectionId == connection.Id && x.CapabilityId == continuation.CapabilityId, cancellationToken);
            if (cursor is null)
            {
                cursor = new GraphCursor
                {
                    WorkspaceId = project.WorkspaceId,
                    EnvironmentId = payload.EnvironmentId,
                    ConnectionId = connection.Id,
                    CapabilityId = continuation.CapabilityId,
                    ScopeHash = continuation.ScopeHash,
                    DeltaLink = continuation.DeltaLink
                };
                complete.GraphCursors.Add(cursor);
            }
            cursor.ScopeHash = continuation.ScopeHash;
            cursor.DeltaLink = continuation.DeltaLink;
            cursor.PublishedAtUtc = DateTimeOffset.UtcNow;
        }
        await complete.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void Check(CollectionRun run, Connection connection, string producerId)
    {
        // A run found for this job, or reused for identical report bytes, must come from this connection and producer.
        if (run.ConnectionId != connection.Id || run.ConfirmedAtUtc is null || run.ProducerId != producerId) { throw new ConflictException(); }
    }

    private async Task<Guid> DownloadReportAsync(JobExecution execution, ImportService imports, GraphTransport transport,
        GraphJobPayload payload, string producerId, Connection connection, Guid projectId, CancellationToken cancellationToken)
    {
        var producer = catalog.RequireProducer(producerId);
        var profile = reports.ForProducer(producer.Id, producer.Version) ?? throw new ContractException("ReportProfileUnavailable", "profile");
        var path = GraphReportDownloads.PathFor(producer.Id) ?? throw new ContractException("ReportDownloadUnavailable", "profile");
        await execution.ReportProgressAsync(0, cancellationToken);
        await execution.Access.RequireAsync(execution.Job.ProjectId, WorkspaceOperation.Collect, cancellationToken);
        byte[] content;
        try
        {
            content = await transport.DownloadReportAsync(path, limits.CompressedBytes, false, cancellationToken)
                ?? throw new GraphReadException("ReportDownloadFailed");
        }
        catch (GraphReadException error)
        {
            // Surface a stable, non-sensitive code on the job; report URLs and bodies are never recorded.
            throw new ContractException(error.Code, "report");
        }
        await execution.ReportProgressAsync(0, cancellationToken);
        return await imports.StageReportAsync(projectId, payload.EnvironmentId, profile, content, connection.Id, execution.Job.Id,
            cancellationToken);
    }

    public static string CursorScopeHash(CollectionScope scope, DiscoveryCatalog catalog) =>
        DiscoveryScope.Hash(new CollectionScope(scope.Kind, scope.Value + "|" + EntraCollector.UserPath(catalog) + "|1.0.0"));
}
