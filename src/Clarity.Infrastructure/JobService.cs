using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class JobService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope)
{
    public async Task EnqueueAsync(Guid projectId, Guid operationId, CollectionJobKind kind, string payloadJson,
        string concurrencyKey, string correlationId, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, kind.Operation(), cancellationToken);
        if (operationId == Guid.Empty || concurrencyKey.Length != 64 || payloadJson.Length > 16000)
        {
            throw new ArgumentException("Invalid job request.");
        }
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PublicationService.LockEnvironmentAsync(db, project.WorkspaceId, operationId, cancellationToken);
        var existing = await db.CollectionJobs.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == operationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ProjectId != projectId || existing.Kind != kind || existing.PayloadJson != payloadJson
                || existing.ConcurrencyKey != concurrencyKey)
            {
                throw new ConflictException();
            }
            return;
        }
        db.CollectionJobs.Add(new CollectionJob
        {
            Id = operationId,
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            Kind = kind,
            PayloadJson = payloadJson,
            ConcurrencyKey = concurrencyKey,
            CorrelationId = correlationId,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CancelAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Collect, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.CollectionJobs.Where(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == projectId && x.Id == jobId
            && x.Kind != CollectionJobKind.Export);
        if (!await query.AnyAsync(cancellationToken)) { throw new AccessDeniedException(); }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await query.Where(x => x.State == CollectionRunState.Pending || x.State == CollectionRunState.Running)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.CancellationRequested, true)
                .SetProperty(x => x.State, x => x.State == CollectionRunState.Pending ? CollectionRunState.Cancelled : x.State), cancellationToken);
        await SynchronizeRunAsync(db, project.WorkspaceId, jobId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RetryAsync(Guid projectId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Collect, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.CollectionJobs.Where(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == project.Id && x.Id == jobId
            && x.Kind != CollectionJobKind.Export && (x.State == CollectionRunState.Failed || x.State == CollectionRunState.Cancelled))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.State, CollectionRunState.Pending)
                .SetProperty(x => x.CancellationRequested, false).SetProperty(x => x.Attempt, 0)
                .SetProperty(x => x.AvailableAtUtc, DateTimeOffset.UtcNow).SetProperty(x => x.ErrorCode, (string?)null)
                .SetProperty(x => x.ActorTenantId, scope.Identity.TenantId).SetProperty(x => x.ActorObjectId, scope.Identity.ObjectId),
                cancellationToken);
        if (changed != 1)
        {
            throw new ConflictException();
        }
        await SynchronizeRunAsync(db, project.WorkspaceId, jobId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static Task SynchronizeRunAsync(ClarityDbContext db, Guid workspaceId, Guid jobId, CancellationToken ct) =>
        db.CollectionRuns.Where(x => x.WorkspaceId == workspaceId && x.Id == jobId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.State,
                x => db.CollectionJobs.Where(job => job.WorkspaceId == workspaceId && job.Id == jobId).Select(job => job.State).First()), ct);
}
