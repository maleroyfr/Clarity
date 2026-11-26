using Clarity.Contracts;
using Clarity.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clarity.Infrastructure;

public sealed record JobExecution(
    CollectionJob Job, IDbContextFactory<ClarityDbContext> Factory, IProjectAccess Access,
    Func<int, CancellationToken, Task> ReportProgressAsync);

public interface ICollectionJobHandler
{
    CollectionJobKind Kind { get; }
    Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken);
}

public sealed class RetryableCollectionException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class CollectionJobProcessor(SqlJobQueue queue, IEnumerable<ICollectionJobHandler> handlers,
    ILogger<CollectionJobProcessor> logger)
{
    public async Task<bool> ProcessOneAsync(CancellationToken stoppingToken)
    {
        var job = await queue.ClaimAsync(stoppingToken);
        if (job is null)
        {
            return false;
        }
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = job.CorrelationId });
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = HeartbeatAsync(job, lease);
        try
        {
            var factory = queue.CreateFactory(job);
            var workspace = new WorkspaceScope(new UserIdentity(job.ActorTenantId, job.ActorObjectId), [job.WorkspaceId]);
            var access = new StoredProjectAccess(factory, workspace);
            await access.RequireAsync(job.ProjectId, job.Kind == CollectionJobKind.Import ? WorkspaceOperation.Import : WorkspaceOperation.Collect, lease.Token);
            var handler = handlers.SingleOrDefault(x => x.Kind == job.Kind)
                ?? throw new InvalidOperationException("Collection handler unavailable.");
            await handler.ExecuteAsync(new JobExecution(job, factory, access, async (progress, token) =>
            {
                if (!await queue.RenewAsync(job, progress, token))
                {
                    throw new OperationCanceledException("Job lease lost or cancellation requested.", token);
                }
            }), lease.Token);
            await FinishAsync(job, null, false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Leave the lease for another process to reclaim after a host interruption.
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(job, "CancelledOrLeaseLost", false, stoppingToken);
        }
        catch (Exception exception)
        {
            var retryable = exception is RetryableCollectionException or SqlException { Number: 1205 or -2 };
            var code = exception switch
            {
                AccessDeniedException => "AccessDenied",
                ContractException contract => contract.Code,
                RetryableCollectionException retry => retry.Code,
                _ => "ProcessingFailed"
            };
            logger.LogWarning("Collection job failed with {ErrorType}; attempt {Attempt}; correlation {CorrelationId}",
                exception.GetType().Name, job.Attempt, job.CorrelationId);
            await FinishAsync(job, code, retryable, stoppingToken);
        }
        finally
        {
            await lease.CancelAsync();
            await heartbeat;
        }
        return true;
    }

    private async Task FinishAsync(CollectionJob job, string? errorCode, bool retryable, CancellationToken ct)
    {
        if (!await queue.FinishAsync(job, errorCode, retryable, ct)) { return; }
        var factory = queue.CreateFactory(job);
        var workspace = new WorkspaceScope(new UserIdentity(job.ActorTenantId, job.ActorObjectId), [job.WorkspaceId]);
        try
        {
            await new StoredProjectAccess(factory, workspace).RequireAsync(job.ProjectId,
                job.Kind == CollectionJobKind.Import ? WorkspaceOperation.Import : WorkspaceOperation.Collect, ct);
        }
        catch (AccessDeniedException)
        {
            logger.LogWarning("Run state synchronization denied; job state remains authoritative; correlation {CorrelationId}", job.CorrelationId);
            return;
        }
        await using var db = await factory.CreateDbContextAsync(ct);
        await JobService.SynchronizeRunAsync(db, job.WorkspaceId, job.Id, ct);
    }

    private async Task HeartbeatAsync(CollectionJob job, CancellationTokenSource lease)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(lease.Token))
            {
                if (!await queue.RenewAsync(job, null, lease.Token))
                {
                    await lease.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (lease.IsCancellationRequested) { }
        catch (SqlException exception)
        {
            logger.LogWarning("Collection lease renewal failed with SQL code {Code}; correlation {CorrelationId}", exception.Number, job.CorrelationId);
            await lease.CancelAsync();
        }
    }
}

public sealed class CollectionWorker(CollectionJobProcessor processor, ILogger<CollectionWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(WorkAsync(stoppingToken), WorkAsync(stoppingToken));

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await processor.ProcessOneAsync(stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (SqlException exception)
            {
                logger.LogError("Collection dispatcher SQL failure {Code}; correlation {CorrelationId}", exception.Number, Guid.NewGuid().ToString("N"));
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
