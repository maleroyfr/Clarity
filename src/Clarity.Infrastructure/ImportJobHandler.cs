using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class ImportJobHandler(IArtifactStore store, ZipPackageReader reader, PackageLimits limits,
    DiscoveryCatalog catalog, PackageValidator validator, ReportProfiles? profiles = null) : ICollectionJobHandler
{
    public CollectionJobKind Kind => CollectionJobKind.Import;

    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        var job = execution.Job;
        var payload = JsonSerializer.Deserialize<ImportJobPayload>(job.PayloadJson, ContractJson.Options)
            ?? throw new ContractException("JobPayload", "job");
        await PublishAsync(execution, payload.RunId, cancellationToken);
    }

    public async Task PublishAsync(JobExecution execution, Guid runId, CancellationToken cancellationToken)
    {
        var job = execution.Job;
        var operation = job.Kind.Operation();
        var project = await execution.Access.RequireAsync(job.ProjectId, operation, cancellationToken);
        var scope = new WorkspaceScope(new UserIdentity(job.ActorTenantId, job.ActorObjectId), [project.WorkspaceId]);
        var imports = new ImportService(execution.Factory, execution.Access, scope, store, reader, limits, catalog, profiles);
        DiscoveryPackage package;
        await using (var db = await execution.Factory.CreateDbContextAsync(cancellationToken))
        {
            var run = await ImportService.RunAsync(db, project, runId, cancellationToken);
            if (run.ConfirmedAtUtc is null) { throw new ConflictException(); }
            var environment = await ImportService.EnvironmentAsync(db, project, run.EnvironmentId, cancellationToken);
            package = await imports.ReadForPublicationAsync(db, project, environment, run, cancellationToken);
            run.State = CollectionRunState.Running;
            await db.SaveChangesAsync(cancellationToken);
        }
        var publisher = new PublicationService(execution.Factory, execution.Access, validator, catalog);
        var progress = 0;
        foreach (var section in package.Results)
        {
            await execution.ReportProgressAsync(progress, cancellationToken);
            await publisher.PublishSectionAsync(job.ProjectId, runId, package, section.SectionId, cancellationToken);
            await execution.ReportProgressAsync(++progress, cancellationToken);
        }
        await execution.Access.RequireAsync(job.ProjectId, operation, cancellationToken);
        await using var completed = await execution.Factory.CreateDbContextAsync(cancellationToken);
        var finished = await ImportService.RunAsync(completed, project, runId, cancellationToken);
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
