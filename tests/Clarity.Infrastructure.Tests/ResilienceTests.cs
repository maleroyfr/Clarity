using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

/// <summary>Export generation under worker restarts, lease reclaim and concurrent consultant changes.</summary>
[Collection("SQL lifecycle")]
public sealed class ResilienceTests
{
    [SqlFact]
    [Trait("Recette", "R08")]
    [Trait("Recette", "R14")]
    public async Task ReclaimedExportRecordsOnlyTheLeaseOwnersFilesAndPurgesInterruptedAttempts()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        await ScopeTests.PublishAsync(fixture);
        var project = fixture.Project.Id;
        var ws = fixture.Project.WorkspaceId;
        var store = new MemoryExportStore();
        var id = await ExportTests.Service(fixture, store).LaunchAsync(project, new(Guid.NewGuid(), new(EnvironmentRole.Source)));
        var time = new ManualClock();
        var queue = new SqlJobQueue(fixture.ConnectionString, time);
        var stale = (await queue.ClaimAsync(default)).ShouldNotBeNull();
        time.Advance(TimeSpan.FromMinutes(1));
        var owner = (await queue.ClaimAsync(default)).ShouldNotBeNull();
        (stale.Id, owner.Id, owner.Attempt).ShouldBe((id, id, 2));
        var handler = new ExportJobHandler(store, new NameText(), DiscoveryCatalog.Load(), time);

        // A killed earlier attempt left a file nobody references; another export's file shares the workspace prefix.
        var orphan = (await store.SaveAsync(ws, id, ".xlsx", "application/octet-stream", new MemoryStream([1]), default)).BlobName;
        var unrelated = (await store.SaveAsync(ws, Guid.NewGuid(), ".csv", "text/csv", new MemoryStream([2]), default)).BlobName;

        // The worker whose lease was reclaimed writes its files, fails at the fence, removes only its own files and records nothing.
        await Should.ThrowAsync<OperationCanceledException>(() => handler.ExecuteAsync(Execution(queue, stale), default));
        store.Names.Order(StringComparer.Ordinal).ShouldBe(new[] { orphan, unrelated }.Order(StringComparer.Ordinal));
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ExportJobs.SingleAsync(x => x.Id == id)).GeneratedAtUtc.ShouldBeNull();
            (await db.ExportArtifacts.CountAsync(x => x.ExportJobId == id)).ShouldBe(0);
        }

        await handler.ExecuteAsync(Execution(queue, owner), default);
        (await queue.FinishAsync(owner, null, false, default)).ShouldBeTrue();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var artifacts = await db.ExportArtifacts.Where(x => x.ExportJobId == id).Select(x => x.BlobName).ToListAsync();
            artifacts.Count.ShouldBe(3);
            store.Names.Order(StringComparer.Ordinal).ShouldBe(artifacts.Append(unrelated).Order(StringComparer.Ordinal));
            (await db.ExportJobs.SingleAsync(x => x.Id == id)).GeneratedAtUtc.ShouldNotBeNull();
            (await db.CollectionJobs.SingleAsync(x => x.Id == id)).State.ShouldBe(CollectionRunState.Completed);
            (await db.AuditEvents.CountAsync(x => x.Action == "ExportGenerated" && x.ResourceId == id)).ShouldBe(1);
        }

        // Replaying a generated export is a no-op.
        await handler.ExecuteAsync(Execution(queue, owner), default);
        store.Count.ShouldBe(4);
    }

    [SqlFact]
    [Trait("Recette", "R14")]
    public async Task DecisionChangedWhileReadingSupersedesTheExportWithoutMixedDateFiles()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var ids = await ScopeTests.PublishAsync(fixture);
        var project = fixture.Project.Id;
        await ScopeTests.Service(fixture).ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Synthetic wave",
            ObjectIds: [ids["alice"]]), 1);
        var store = new MemoryExportStore();
        var exports = ExportTests.Service(fixture, store);
        var id = await exports.LaunchAsync(project, new(Guid.NewGuid(), new(EnvironmentRole.Source)));
        var text = new ChangingText(fixture);

        (await new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, TimeProvider.System),
            [new ExportJobHandler(store, text, DiscoveryCatalog.Load())], NullLogger<CollectionJobProcessor>.Instance).ProcessOneAsync(default))
            .ShouldBeTrue();

        text.Changed.ShouldBeTrue();
        var row = (await exports.PageAsync(project, EnvironmentRole.Source, new())).Items.ShouldHaveSingleItem();
        (row.Id, row.State, row.ErrorCode, row.GeneratedAtUtc, row.Artifacts.Count)
            .ShouldBe((id, CollectionRunState.Failed, (string?)"SelectionSuperseded", (DateTimeOffset?)null, 0));
        store.Count.ShouldBe(0);
    }

    private static JobExecution Execution(SqlJobQueue queue, CollectionJob job)
    {
        var factory = queue.CreateFactory(job);
        var access = new StoredProjectAccess(factory, new WorkspaceScope(new UserIdentity(job.ActorTenantId, job.ActorObjectId), [job.WorkspaceId]));
        return new JobExecution(job, factory, access, (_, _) => Task.CompletedTask);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(1);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class NameText : IExportText
    {
        public string this[string key] => key;
    }

    /// <summary>Changes a scope decision the first time a label is read, i.e. between the two selection checks of the handler.</summary>
    private sealed class ChangingText(DiscoveryFixture fixture) : IExportText
    {
        public bool Changed { get; private set; }

        public string this[string key]
        {
            get
            {
                if (!Changed)
                {
                    Changed = true;
                    using var db = fixture.Factory.CreateDbContext();
                    db.ScopeDecisions.ExecuteUpdate(update => update.SetProperty(d => d.Revision, d => d.Revision + 1)).ShouldBe(1);
                }
                return key;
            }
        }
    }
}
