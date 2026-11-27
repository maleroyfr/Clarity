using Clarity.Contracts;
using Clarity.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

/// <summary>
/// Requests the controlled deletion of an archived project. In one transaction the project becomes Deleting (no longer readable), its other
/// jobs are cancelled and one Deletion job is queued; the worker then removes Blob files, SQL rows and finally the project itself.
/// Requesting again while the job is queued or running changes nothing; after a failed job it queues a new attempt.
/// </summary>
public sealed class ProjectDeletionService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope,
    IExportStore exports)
{
    public async Task RequestAsync(Guid projectId, string confirmationName, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.DeleteProject, cancellationToken);
        if (!string.Equals(project.Name, confirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("The confirmation does not match the project name.");
        }
        var ws = project.WorkspaceId;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        // Deletion runs on the SQL job queue against Blob storage; without them nothing could complete the request.
        if (!exports.IsConfigured || !db.Database.IsSqlServer())
        {
            throw new DiscoveryConfigurationException();
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PublicationService.LockEnvironmentAsync(db, ws, project.Id, cancellationToken);
        var tracked = await db.Projects.SingleAsync(x => x.WorkspaceId == ws && x.Id == project.Id, cancellationToken);
        var previous = await db.CollectionJobs.Where(x => x.WorkspaceId == ws && x.ProjectId == project.Id && x.Kind == CollectionJobKind.Deletion)
            .ToListAsync(cancellationToken);
        if (previous.Any(x => x.State is CollectionRunState.Pending or CollectionRunState.Running))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        db.CollectionJobs.RemoveRange(previous);
        var action = tracked.Status == ProjectStatus.Deleting ? "ProjectDeletionRetried" : "ProjectDeletionRequested";
        tracked.BeginDeletion();
        await DeletionJobHandler.CancelOtherJobsAsync(db, ws, project.Id, null, cancellationToken);
        var correlation = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        db.CollectionJobs.Add(new CollectionJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = ws,
            ProjectId = project.Id,
            Kind = CollectionJobKind.Deletion,
            PayloadJson = "{}",
            ConcurrencyKey = DiscoveryScope.Hash(new CollectionScope("deletion", $"{ws:N}:{project.Id:N}")),
            CorrelationId = correlation,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId
        });
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = ws,
            ResourceId = project.Id,
            Action = action,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            CorrelationId = correlation
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary>
/// Deletes a project marked Deleting, consistently across Blob storage and SQL. It first waits for the project's other jobs to stop, then
/// deletes every export and raw file, then deletes SQL rows child-first in bounded batches, and finally removes the jobs, environments and
/// project with a "ProjectDeleted" audit event in one fenced transaction. Every step is idempotent, so a failure at any point leaves the
/// project Deleting (unreadable) and the job retries from the start; a file is never left referenced by a surviving row it no longer backs,
/// because the rows that reference files are only removed after the files.
/// </summary>
public sealed class DeletionJobHandler(IArtifactStore artifacts, IExportStore exports, TimeProvider? clock = null) : ICollectionJobHandler
{
    internal const int BatchSize = 4000;
    internal static readonly TimeSpan DrainTimeout = TimeSpan.FromMinutes(2);
    private static readonly IReadOnlySet<string> KeepNothing = new HashSet<string>();
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public CollectionJobKind Kind => CollectionJobKind.Deletion;

    /// <summary>Workspace-level tables a project deletion keeps: they belong to the client workspace, not to one project.</summary>
    internal static readonly IReadOnlySet<Type> WorkspaceLevel = new HashSet<Type>
    {
        typeof(ClientWorkspace), typeof(WorkspaceMember), typeof(PlatformAdmin), typeof(Connection), typeof(AuditEvent)
    };

    /// <summary>Tables removed by the final fenced transaction rather than by a batch step.</summary>
    internal static readonly IReadOnlyList<Type> FinalStep = [typeof(CollectionJob), typeof(ProjectEnvironment), typeof(Project)];

    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        await DrainAsync(execution, cancellationToken);
        await DeleteFilesAsync(execution, cancellationToken);
        await DeleteRowsAsync(execution, cancellationToken);
        await DeleteProjectAsync(execution, cancellationToken);
    }

    internal static Task<int> CancelOtherJobsAsync(ClarityDbContext db, Guid ws, Guid projectId, Guid? keep, CancellationToken ct) =>
        db.CollectionJobs.Where(x => x.WorkspaceId == ws && x.ProjectId == projectId && x.Kind != CollectionJobKind.Deletion && x.Id != keep
                && (x.State == CollectionRunState.Pending || x.State == CollectionRunState.Running))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.CancellationRequested, true)
                .SetProperty(x => x.State, x => x.State == CollectionRunState.Pending ? CollectionRunState.Cancelled : x.State), ct);

    private async Task DrainAsync(JobExecution execution, CancellationToken ct)
    {
        var job = execution.Job;
        var deadline = time.GetUtcNow() + DrainTimeout;
        while (true)
        {
            await using (var db = await execution.Factory.CreateDbContextAsync(ct))
            {
                await CancelOtherJobsAsync(db, job.WorkspaceId, job.ProjectId, job.Id, ct);
                var now = time.GetUtcNow();
                if (!await OtherRunning(db, job, now).AnyAsync(ct)) { return; }
                if (now >= deadline) { throw new RetryableCollectionException("ProjectJobsRunning"); }
            }
            await execution.ReportProgressAsync(1, ct);
            await Task.Delay(TimeSpan.FromSeconds(2), time, ct);
        }
    }

    private static IQueryable<CollectionJob> OtherRunning(ClarityDbContext db, CollectionJob job, DateTimeOffset now) =>
        db.CollectionJobs.Where(x => x.WorkspaceId == job.WorkspaceId && x.ProjectId == job.ProjectId && x.Id != job.Id
            && x.State == CollectionRunState.Running && x.LeaseExpiresAtUtc > now);

    private async Task DeleteFilesAsync(JobExecution execution, CancellationToken ct)
    {
        var (ws, projectId) = (execution.Job.WorkspaceId, execution.Job.ProjectId);
        List<Guid> exportIds;
        List<string> rawFiles;
        await using (var db = await execution.Factory.CreateDbContextAsync(ct))
        {
            exportIds = await db.ExportJobs.Where(x => x.WorkspaceId == ws && x.ProjectId == projectId).Select(x => x.Id).ToListAsync(ct);
            var runs = db.CollectionRuns.Where(r => r.WorkspaceId == ws && r.ProjectId == projectId).Select(r => r.Id);
            rawFiles = await db.RawArtifacts.Where(a => a.WorkspaceId == ws && runs.Contains(a.RunId)).Select(a => a.BlobName).ToListAsync(ct);
        }
        foreach (var exportId in exportIds)
        {
            await StorageAsync(() => exports.PurgeExportAsync(ws, exportId, KeepNothing, ct));
        }
        foreach (var name in rawFiles)
        {
            await StorageAsync(() => artifacts.DeleteAsync(ws, name, ct));
        }
        await execution.ReportProgressAsync(30, ct);
    }

    private static async Task StorageAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is Azure.RequestFailedException or IOException or HttpRequestException)
        {
            throw new RetryableCollectionException("StorageUnavailable");
        }
    }

    private static async Task DeleteRowsAsync(JobExecution execution, CancellationToken ct)
    {
        var (ws, projectId) = (execution.Job.WorkspaceId, execution.Job.ProjectId);
        for (var index = 0; index < Steps.Count; index++)
        {
            int deleted;
            do
            {
                await using var db = await execution.Factory.CreateDbContextAsync(ct);
                deleted = await BlockedAsRetryable(() => Steps[index].DeleteBatchAsync(db, ws, projectId, ct));
            }
            while (deleted == BatchSize);
            await execution.ReportProgressAsync(30 + 65 * (index + 1) / Steps.Count, ct);
        }
    }

    private async Task DeleteProjectAsync(JobExecution execution, CancellationToken ct)
    {
        var job = execution.Job;
        var (ws, projectId) = (job.WorkspaceId, job.ProjectId);
        await using var db = await execution.Factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow();
        await SqlJobQueue.FenceAsync(db, job, now, ct);
        if (await OtherRunning(db, job, now).AnyAsync(ct)) { throw new RetryableCollectionException("ProjectJobsRunning"); }
        await BlockedAsRetryable(async () =>
        {
            var deleted = await db.CollectionJobs.Where(x => x.WorkspaceId == ws && x.ProjectId == projectId).ExecuteDeleteAsync(ct);
            deleted += await db.Environments.Where(x => x.WorkspaceId == ws && x.ProjectId == projectId).ExecuteDeleteAsync(ct);
            return deleted + await db.Projects.Where(x => x.WorkspaceId == ws && x.Id == projectId).ExecuteDeleteAsync(ct);
        });
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = ws,
            ResourceId = projectId,
            Action = "ProjectDeleted",
            ActorTenantId = job.ActorTenantId,
            ActorObjectId = job.ActorObjectId,
            CorrelationId = job.CorrelationId
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>A row inserted by a job that raced the deletion still references a parent; the whole deletion is retried.</summary>
    private static async Task<int> BlockedAsRetryable(Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is SqlException { Number: 547 } or DbUpdateException { InnerException: SqlException { Number: 547 } })
        {
            throw new RetryableCollectionException("DeletionBlocked");
        }
    }

    internal sealed record DeletionStep(Type Entity, Func<ClarityDbContext, Guid, Guid, CancellationToken, Task<int>> DeleteBatchAsync);

    private static DeletionStep Step<T>(Func<ClarityDbContext, Guid, Guid, IQueryable<T>> rows) where T : class =>
        new(typeof(T), (db, ws, projectId, ct) => rows(db, ws, projectId).Take(BatchSize).ExecuteDeleteAsync(ct));

    private static IQueryable<Guid> Environments(ClarityDbContext db, Guid ws, Guid projectId) =>
        db.Environments.Where(x => x.WorkspaceId == ws && x.ProjectId == projectId).Select(x => x.Id);

    private static IQueryable<Guid> Runs(ClarityDbContext db, Guid ws, Guid projectId) =>
        db.CollectionRuns.Where(x => x.WorkspaceId == ws && x.ProjectId == projectId).Select(x => x.Id);

    private static IQueryable<Guid> Objects(ClarityDbContext db, Guid ws, Guid projectId)
    {
        var environments = Environments(db, ws, projectId);
        return db.InventoryObjects.Where(x => x.WorkspaceId == ws && environments.Contains(x.EnvironmentId)).Select(x => x.Id);
    }

    private static IQueryable<Guid> Sections(ClarityDbContext db, Guid ws, Guid projectId)
    {
        var runs = Runs(db, ws, projectId);
        return db.SectionRuns.Where(x => x.WorkspaceId == ws && runs.Contains(x.RunId)).Select(x => x.Id);
    }

    /// <summary>Every project-owned table, children before the parents their foreign keys reference.</summary>
    internal static readonly IReadOnlyList<DeletionStep> Steps =
    [
        Step((db, ws, p) => { var parents = db.RunComparisons.Where(x => x.WorkspaceId == ws && x.ProjectId == p).Select(x => x.Id);
            return db.RunComparisonItems.Where(x => x.WorkspaceId == ws && parents.Contains(x.ComparisonId)); }),
        Step((db, ws, p) => db.RunComparisons.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => { var parents = db.TargetComparisons.Where(x => x.WorkspaceId == ws && x.ProjectId == p).Select(x => x.Id);
            return db.TargetComparisonItems.Where(x => x.WorkspaceId == ws && parents.Contains(x.ComparisonId)); }),
        Step((db, ws, p) => db.TargetComparisons.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => db.TargetMappingRules.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => { var parents = db.DependencyAnalyses.Where(x => x.WorkspaceId == ws && x.ProjectId == p).Select(x => x.Id);
            return db.DependencyLinks.Where(x => x.WorkspaceId == ws && parents.Contains(x.AnalysisId)); }),
        Step((db, ws, p) => { var parents = db.DependencyAnalyses.Where(x => x.WorkspaceId == ws && x.ProjectId == p).Select(x => x.Id);
            return db.DependencyResources.Where(x => x.WorkspaceId == ws && parents.Contains(x.AnalysisId)); }),
        Step((db, ws, p) => db.DependencyAnalyses.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => { var parents = db.ScopeDecisions.Where(x => x.WorkspaceId == ws && x.ProjectId == p).Select(x => x.Id);
            return db.ScopeDecisionRevisions.Where(x => x.WorkspaceId == ws && parents.Contains(x.DecisionId)); }),
        Step((db, ws, p) => db.ScopeDecisions.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => { var parents = db.Populations.Where(x => x.WorkspaceId == ws && x.ProjectId == p).Select(x => x.Id);
            return db.PopulationEntries.Where(x => x.WorkspaceId == ws && parents.Contains(x.PopulationId)); }),
        Step((db, ws, p) => { var parents = db.Populations.Where(x => x.WorkspaceId == ws && x.ProjectId == p).Select(x => x.Id);
            return db.PopulationMembers.Where(x => x.WorkspaceId == ws && parents.Contains(x.PopulationId)); }),
        Step((db, ws, p) => db.Populations.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => { var parents = db.MappingDecisions.Where(x => x.WorkspaceId == ws && x.ProjectId == p).Select(x => x.Id);
            return db.MappingRevisions.Where(x => x.WorkspaceId == ws && parents.Contains(x.DecisionId)); }),
        Step((db, ws, p) => db.MappingDecisions.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => db.SyncBindings.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => db.Assessments.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => db.ReviewFlags.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => { var environments = Environments(db, ws, p);
            var parents = db.InventoryRelationships.Where(x => x.WorkspaceId == ws && environments.Contains(x.EnvironmentId)).Select(x => x.Id);
            return db.RelationshipSupports.Where(x => x.WorkspaceId == ws && parents.Contains(x.RelationshipId)); }),
        Step((db, ws, p) => { var sections = Sections(db, ws, p);
            return db.RelationshipObservations.Where(x => x.WorkspaceId == ws && sections.Contains(x.SectionRunId)); }),
        Step((db, ws, p) => { var environments = Environments(db, ws, p);
            return db.InventoryRelationships.Where(x => x.WorkspaceId == ws && environments.Contains(x.EnvironmentId)); }),
        Step((db, ws, p) => { var environments = Environments(db, ws, p);
            return db.AlternateKeyPeriods.Where(x => x.WorkspaceId == ws && environments.Contains(x.EnvironmentId)); }),
        Step((db, ws, p) => { var objects = Objects(db, ws, p); return db.CurrentValues.Where(x => x.WorkspaceId == ws && objects.Contains(x.ObjectId)); }),
        Step((db, ws, p) => { var environments = Environments(db, ws, p);
            return db.ExternalKeys.Where(x => x.WorkspaceId == ws && environments.Contains(x.EnvironmentId)); }),
        Step((db, ws, p) => { var objects = Objects(db, ws, p);
            return db.ObjectObservations.Where(x => x.WorkspaceId == ws && objects.Contains(x.ObjectId)); }),
        Step((db, ws, p) => { var environments = Environments(db, ws, p);
            return db.InventoryObjects.Where(x => x.WorkspaceId == ws && environments.Contains(x.EnvironmentId)); }),
        Step((db, ws, p) => { var runs = Runs(db, ws, p); return db.RawArtifacts.Where(x => x.WorkspaceId == ws && runs.Contains(x.RunId)); }),
        Step((db, ws, p) => { var runs = Runs(db, ws, p); return db.SectionRuns.Where(x => x.WorkspaceId == ws && runs.Contains(x.RunId)); }),
        Step((db, ws, p) => db.CollectionRuns.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => { var environments = Environments(db, ws, p);
            return db.GraphCursors.Where(x => x.WorkspaceId == ws && environments.Contains(x.EnvironmentId)); }),
        Step((db, ws, p) => db.CollectionRequests.Where(x => x.WorkspaceId == ws && x.ProjectId == p)),
        Step((db, ws, p) => { var parents = db.ExportJobs.Where(x => x.WorkspaceId == ws && x.ProjectId == p).Select(x => x.Id);
            return db.ExportArtifacts.Where(x => x.WorkspaceId == ws && parents.Contains(x.ExportJobId)); }),
        Step((db, ws, p) => db.ExportJobs.Where(x => x.WorkspaceId == ws && x.ProjectId == p))
    ];
}
