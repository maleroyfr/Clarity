using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class ImportJobHandler(IArtifactStore store, ZipPackageReader reader, PackageLimits limits,
    DiscoveryCatalog catalog, PackageValidator validator) : ICollectionJobHandler
{
    public CollectionJobKind Kind => CollectionJobKind.Import;

    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        var job = execution.Job;
        var payload = JsonSerializer.Deserialize<ImportJobPayload>(job.PayloadJson, ContractJson.Options)
            ?? throw new ContractException("JobPayload", "job");
        var project = await execution.Access.RequireAsync(job.ProjectId, WorkspaceOperation.Import, cancellationToken);
        var scope = new WorkspaceScope(new UserIdentity(job.ActorTenantId, job.ActorObjectId), [project.WorkspaceId]);
        var imports = new ImportService(execution.Factory, execution.Access, scope, store, reader, limits, catalog);
        DiscoveryPackage package;
        await using (var db = await execution.Factory.CreateDbContextAsync(cancellationToken))
        {
            var run = await ImportService.RunAsync(db, project, payload.RunId, cancellationToken);
            if (run.ConfirmedAtUtc is null) { throw new ConflictException(); }
            var environment = await ImportService.EnvironmentAsync(db, project, run.EnvironmentId, cancellationToken);
            package = await imports.ReadAsync(db, run, cancellationToken);
            catalog.ValidatePackage(package);
            await ImportService.ValidateContextAsync(db, project, environment, package, cancellationToken);
            run.State = CollectionRunState.Running;
            await db.SaveChangesAsync(cancellationToken);
        }
        var publisher = new PublicationService(execution.Factory, execution.Access, validator);
        var progress = 0;
        foreach (var section in package.Results)
        {
            await execution.ReportProgressAsync(progress, cancellationToken);
            await publisher.PublishSectionAsync(job.ProjectId, payload.RunId, package, section.SectionId, cancellationToken);
            await execution.ReportProgressAsync(++progress, cancellationToken);
        }
        await execution.Access.RequireAsync(job.ProjectId, WorkspaceOperation.Import, cancellationToken);
        await using var completed = await execution.Factory.CreateDbContextAsync(cancellationToken);
        var finished = await ImportService.RunAsync(completed, project, payload.RunId, cancellationToken);
        finished.State = CollectionRunState.Completed;
        if (finished.RequestId is { } requestId)
        {
            var request = await completed.CollectionRequests.SingleAsync(x => x.WorkspaceId == project.WorkspaceId
                && x.Id == requestId, cancellationToken);
            request.State = CollectionRequestState.Integrated;
        }
        completed.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = project.WorkspaceId,
            ActorTenantId = job.ActorTenantId,
            ActorObjectId = job.ActorObjectId,
            Action = "CollectionIntegrated",
            ResourceId = finished.Id,
            CorrelationId = job.CorrelationId
        });
        await completed.SaveChangesAsync(cancellationToken);
    }
}
