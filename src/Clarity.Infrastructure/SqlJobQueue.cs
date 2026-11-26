using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class SqlJobQueue(string connectionString, TimeProvider time)
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(45);
    private readonly JobContextFactory dispatcher = new(connectionString, new JobDispatchScope());

    public JobContextFactory CreateFactory(CollectionJob job) => new(connectionString,
        new WorkspaceScope(new UserIdentity(job.ActorTenantId, job.ActorObjectId), [job.WorkspaceId]));

    public async Task<CollectionJob?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var db = await dispatcher.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = 'clarity:job-claims',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Job claim lock unavailable.', 1;
            """, cancellationToken);
        var now = time.GetUtcNow();
        await db.CollectionJobs.Where(x => !x.CancellationRequested && x.State == CollectionRunState.Running && x.LeaseExpiresAtUtc <= now
            && x.Attempt >= MaxAttempts).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.State, CollectionRunState.Failed).SetProperty(x => x.ErrorCode, "LeaseExpired")
                .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTimeOffset?)null), cancellationToken);
        await db.CollectionJobs.Where(x => x.CancellationRequested
            && (x.State == CollectionRunState.Pending || x.State == CollectionRunState.Running && x.LeaseExpiresAtUtc <= now))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.State, CollectionRunState.Cancelled)
                .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtUtc, (DateTimeOffset?)null), cancellationToken);
        var job = await db.CollectionJobs.Where(x => !x.CancellationRequested && x.Attempt < MaxAttempts
            && ((x.State == CollectionRunState.Pending && x.AvailableAtUtc <= now)
                || (x.State == CollectionRunState.Running && x.LeaseExpiresAtUtc <= now))
            && !db.CollectionJobs.Any(other => other.ConcurrencyKey == x.ConcurrencyKey && other.State == CollectionRunState.Running
                && other.LeaseExpiresAtUtc > now))
            .OrderBy(x => x.AvailableAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (job is not null)
        {
            job.State = CollectionRunState.Running;
            job.Attempt++;
            job.LeaseToken = Guid.NewGuid();
            job.LeaseExpiresAtUtc = now + LeaseDuration;
            job.ErrorCode = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public async Task<bool> RenewAsync(CollectionJob job, int? progress, CancellationToken cancellationToken)
    {
        await using var db = await dispatcher.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        return await OwnedLease(db, job, now).Where(x => !x.CancellationRequested)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseExpiresAtUtc, now + LeaseDuration)
                .SetProperty(x => x.Progress, x => progress.HasValue && progress.Value > x.Progress ? progress.Value : x.Progress),
                cancellationToken) == 1;
    }

    public async Task FinishAsync(CollectionJob job, string? errorCode, bool retryable, CancellationToken cancellationToken)
    {
        await using var db = await dispatcher.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var state = errorCode is null ? CollectionRunState.Completed
            : retryable && job.Attempt < MaxAttempts ? CollectionRunState.Pending : CollectionRunState.Failed;
        await OwnedLease(db, job, now).ExecuteUpdateAsync(update => update
            .SetProperty(x => x.State, x => x.CancellationRequested ? CollectionRunState.Cancelled : state)
            .SetProperty(x => x.ErrorCode, errorCode)
            .SetProperty(x => x.AvailableAtUtc, now.AddSeconds(Math.Pow(2, job.Attempt) * 5))
            .SetProperty(x => x.LeaseToken, (Guid?)null)
            .SetProperty(x => x.LeaseExpiresAtUtc, (DateTimeOffset?)null), cancellationToken);
    }

    public static IQueryable<CollectionJob> OwnedLease(ClarityDbContext db, CollectionJob job, DateTimeOffset now) =>
        db.CollectionJobs.Where(x => x.WorkspaceId == job.WorkspaceId && x.Id == job.Id
            && x.State == CollectionRunState.Running && x.LeaseToken == job.LeaseToken && x.LeaseExpiresAtUtc > now);
}
