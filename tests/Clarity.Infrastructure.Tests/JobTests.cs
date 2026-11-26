using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class JobTests
{
    [SqlFact]
    public async Task CompetingClaimsLeaseExpiryAndStaleCompletionAreFenced()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var id = Guid.NewGuid();
        await EnqueueAsync(fixture, id);
        var time = new ManualClock();
        var queue = new SqlJobQueue(fixture.ConnectionString, time);
        var claims = await Task.WhenAll(queue.ClaimAsync(default), queue.ClaimAsync(default));
        var first = claims.Where(x => x is not null).ShouldHaveSingleItem()!;
        first.Attempt.ShouldBe(1);
        (await queue.RenewAsync(first, 1, default)).ShouldBeTrue();
        time.Advance(TimeSpan.FromMinutes(1));
        var reclaimed = (await queue.ClaimAsync(default)).ShouldNotBeNull();
        reclaimed.Id.ShouldBe(first.Id);
        reclaimed.LeaseToken.ShouldNotBe(first.LeaseToken);
        reclaimed.Progress.ShouldBe(1);
        await queue.FinishAsync(first, null, false, default);
        (await queue.RenewAsync(first, 99, default)).ShouldBeFalse();
        await queue.FinishAsync(reclaimed, null, false, default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        var job = await db.CollectionJobs.SingleAsync();
        job.State.ShouldBe(CollectionRunState.Completed);
        job.Progress.ShouldBe(1);
        job.Attempt.ShouldBe(2);
    }

    [SqlFact]
    public async Task RestartDuringProcessingResumesPublishedSectionWithoutDuplicates()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var package = DiscoveryFixture.Package();
        var run = await fixture.StageAsync(package);
        await EnqueueAsync(fixture, run.Id);
        var time = new ManualClock();
        var queue = new SqlJobQueue(fixture.ConnectionString, time);
        var handler = new RestartHandler(package, run.Id);
        var processor = new CollectionJobProcessor(queue, [handler], NullLogger<CollectionJobProcessor>.Instance);
        using var interruption = new CancellationTokenSource();
        var running = processor.ProcessOneAsync(interruption.Token);
        await handler.Published.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await interruption.CancelAsync();
        await running;
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.CollectionJobs.SingleAsync()).State.ShouldBe(CollectionRunState.Running);
            (await db.ObjectObservations.CountAsync()).ShouldBe(1);
        }
        time.Advance(TimeSpan.FromMinutes(1));
        var restarted = new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, time),
            [handler], NullLogger<CollectionJobProcessor>.Instance);
        (await restarted.ProcessOneAsync(default)).ShouldBeTrue();
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.CollectionJobs.SingleAsync()).State.ShouldBe(CollectionRunState.Completed);
        (await check.ObjectObservations.CountAsync()).ShouldBe(1);
        (await check.InventoryObjects.CountAsync()).ShouldBe(1);
    }

    [SqlFact]
    public async Task BoundedRetryCancellationAndMembershipRevocationArePersisted()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var id = Guid.NewGuid();
        await EnqueueAsync(fixture, id);
        var time = new ManualClock();
        var queue = new SqlJobQueue(fixture.ConnectionString, time);
        var processor = new CollectionJobProcessor(queue, [new FailureHandler()], NullLogger<CollectionJobProcessor>.Instance);
        for (var attempt = 0; attempt < SqlJobQueue.MaxAttempts; attempt++)
        {
            (await processor.ProcessOneAsync(default)).ShouldBeTrue();
            time.Advance(TimeSpan.FromMinutes(1));
        }
        (await processor.ProcessOneAsync(default)).ShouldBeFalse();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var job = await db.CollectionJobs.SingleAsync();
            job.State.ShouldBe(CollectionRunState.Failed);
            job.ErrorCode.ShouldBe("Throttled");
        }
        await fixture.Jobs.RetryAsync(fixture.Project.Id, id);
        await fixture.Jobs.CancelAsync(fixture.Project.Id, id);
        (await queue.ClaimAsync(default)).ShouldBeNull();
        await fixture.Jobs.RetryAsync(fixture.Project.Id, id);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        }
        await processor.ProcessOneAsync(default);
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.CollectionJobs.SingleAsync()).ErrorCode.ShouldBe("AccessDenied");
    }

    [SqlFact]
    public async Task ProviderConcurrencyAndDispatcherScopeDoNotExposeInventory()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        await EnqueueAsync(fixture, Guid.NewGuid());
        await EnqueueAsync(fixture, Guid.NewGuid());
        var queue = new SqlJobQueue(fixture.ConnectionString, new ManualClock());
        (await queue.ClaimAsync(default)).ShouldNotBeNull();
        (await queue.ClaimAsync(default)).ShouldBeNull();
        await using (var dispatch = PersistenceTests.Create(fixture.ConnectionString, []))
        {
            await dispatch.Database.OpenConnectionAsync();
            await dispatch.Database.ExecuteSqlAsync($"EXEC sys.sp_set_session_context @key=N'clarity.jobDispatcher', @value=1");
            (await dispatch.CollectionJobs.IgnoreQueryFilters().CountAsync()).ShouldBe(2);
            (await dispatch.Projects.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
            (await dispatch.InventoryObjects.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        }
        await using var ordinary = PersistenceTests.Create(fixture.ConnectionString, []);
        (await ordinary.CollectionJobs.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    private static Task EnqueueAsync(DiscoveryFixture fixture, Guid id) =>
        fixture.Jobs.EnqueueAsync(fixture.Project.Id, id, CollectionJobKind.Import, "{}",
            new string('a', 64), Guid.NewGuid().ToString("N"));

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(1);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class FailureHandler : ICollectionJobHandler
    {
        public CollectionJobKind Kind => CollectionJobKind.Import;
        public Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken) =>
            throw new RetryableCollectionException("Throttled");
    }

    private sealed class RestartHandler(DiscoveryPackage package, Guid runId) : ICollectionJobHandler
    {
        private int attempts;
        public TaskCompletionSource Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CollectionJobKind Kind => CollectionJobKind.Import;
        public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
        {
            await execution.ReportProgressAsync(0, cancellationToken);
            var publisher = new PublicationService(execution.Factory, execution.Access, new PackageValidator());
            await publisher.PublishSectionAsync(execution.Job.ProjectId, runId, package, package.Results[0].SectionId, cancellationToken);
            Published.TrySetResult();
            if (Interlocked.Increment(ref attempts) == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            await execution.ReportProgressAsync(1, cancellationToken);
        }
    }
}
