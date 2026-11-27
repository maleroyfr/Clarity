using System.Globalization;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class RetentionDeletionTests
{
    [Fact]
    public void DeletionStepsCoverEveryProjectOwnedTableChildrenFirst()
    {
        using var db = new ClarityDbContext(new DbContextOptionsBuilder<ClarityDbContext>().UseSqlServer("Server=unused").Options,
            new WorkspaceScope(new UserIdentity(Guid.Empty, Guid.Empty), []));
        List<Type> order = [.. DeletionJobHandler.Steps.Select(x => x.Entity), .. DeletionJobHandler.FinalStep];
        order.Distinct().Count().ShouldBe(order.Count);
        order.ShouldNotContain(x => DeletionJobHandler.WorkspaceLevel.Contains(x));
        var entities = db.Model.GetEntityTypes().ToList();
        entities.Select(x => x.ClrType.Name).Order(StringComparer.Ordinal)
            .ShouldBe(order.Concat(DeletionJobHandler.WorkspaceLevel).Select(x => x.Name).Order(StringComparer.Ordinal));
        foreach (var entity in entities)
        {
            foreach (var key in entity.GetForeignKeys())
            {
                var principal = key.PrincipalEntityType.ClrType;
                if (DeletionJobHandler.WorkspaceLevel.Contains(principal)) { continue; }
                DeletionJobHandler.WorkspaceLevel.ShouldNotContain(entity.ClrType, $"{entity.ClrType.Name} must not reference project data");
                order.IndexOf(principal).ShouldBeGreaterThan(order.IndexOf(entity.ClrType),
                    $"{entity.ClrType.Name} must be deleted before {principal.Name}");
            }
        }
    }

    [Fact]
    public void OnlyArchivedProjectsCanBeDeletedAndBecomeDueAfterTheConfiguredDelay()
    {
        var options = new RetentionOptions { ArchivedProjectDays = 10 };
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var project = new Project { WorkspaceId = Guid.NewGuid(), Name = "Synthetic lifecycle" };
        options.DeletionDue(project, now).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(project.BeginDeletion);
        ProjectLifecycle.Permits(ProjectStatus.Active, true, WorkspaceOperation.DeleteProject).ShouldBeFalse();

        project.Archive(now.AddDays(-9));
        project.Archive(now);
        project.ArchivedAtUtc.ShouldBe(now.AddDays(-9));
        options.DeletionDue(project, now).ShouldBeFalse();
        options.DeletionDue(project, now.AddDays(1)).ShouldBeTrue();
        ProjectLifecycle.Permits(ProjectStatus.Archived, true, WorkspaceOperation.Read).ShouldBeTrue();
        ProjectLifecycle.Permits(ProjectStatus.Archived, true, WorkspaceOperation.DeleteProject).ShouldBeTrue();
        ProjectLifecycle.Permits(ProjectStatus.Archived, true, WorkspaceOperation.Qualify).ShouldBeFalse();

        project.BeginDeletion();
        project.Archive();
        project.Status.ShouldBe(ProjectStatus.Deleting);
        options.DeletionDue(project, now.AddDays(1)).ShouldBeFalse();
        ProjectLifecycle.Permits(ProjectStatus.Deleting, true, WorkspaceOperation.Read).ShouldBeFalse();
        ProjectLifecycle.Permits(ProjectStatus.Deleting, true, WorkspaceOperation.DeleteProject).ShouldBeTrue();
    }

    [SqlFact]
    public async Task DeletingAnArchivedProjectRemovesItsRowsAndFilesAfterAStorageFailureAndSparesEverythingElse()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.WorkspaceManager));
        }
        var artifacts = new MemoryArtifactStore();
        var exports = new MemoryExportStore();
        var survivor = new Project { WorkspaceId = fixture.Workspace.Id, Name = "Surviving synthetic audit" };
        var survivorEnvironment = new ProjectEnvironment
        {
            WorkspaceId = fixture.Workspace.Id,
            ProjectId = survivor.Id,
            ExternalId = "forest.example.test",
            Kind = EnvironmentKind.AdForest
        };
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.AddRange(survivor, survivorEnvironment);
            await db.SaveChangesAsync();
        }
        await UploadAsync(fixture, artifacts, survivor.Id, survivorEnvironment.Id, Package("survivor-user"), process: true);
        await ExportAsync(fixture, exports, survivor.Id);
        var other = await fixture.OtherWorkspaceAsync();
        var baseline = await CountsAsync(fixture.Factory);
        var otherBaseline = await CountsAsync(other.Factory);
        var survivorFiles = artifacts.Names.Concat(exports.Names).ToList();
        survivorFiles.Count.ShouldBeGreaterThan(1);

        var project = fixture.Project.Id;
        var environment = fixture.Environment.Id;
        await UploadAsync(fixture, artifacts, project, environment, Package("doomed-user"), process: true);
        var ids = await ScopeTests.PublishAsync(fixture);
        var scope = ScopeTests.Service(fixture);
        await scope.CreatePopulationAsync(project, new(Guid.NewGuid(), environment, "Sales group", PopulationKind.Group, RootObjectId: ids["sales"]));
        await scope.ImportAsync(project, new(Guid.NewGuid(), environment, "UPN list", ImportIdentifier.UserPrincipalName, null,
            "alice@example.test\nghost@example.test"));
        await scope.ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Synthetic wave", ObjectIds: [ids["alice"], ids["bob"]]), 2);
        await ExportAsync(fixture, exports, project);
        await new TargetComparisonService(fixture.Factory, fixture.Access, fixture.Scope, DiscoveryCatalog.Load()).AddRuleAsync(project,
            new("user", TargetRuleKind.ComputedUpn, "userPrincipalName", "", "target.example", 1, "Synthetic target UPN"), Guid.NewGuid());
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.Environments.Add(new ProjectEnvironment
            {
                WorkspaceId = fixture.Workspace.Id,
                ProjectId = project,
                Kind = EnvironmentKind.M365Tenant,
                Role = EnvironmentRole.Target,
                TargetState = TargetState.Planned,
                ExternalId = "",
                Hypotheses = "Synthetic tenant to create"
            });
            await db.SaveChangesAsync();
        }
        var populated = await CountsAsync(fixture.Factory);
        string[] expectedGrowth = ["InventoryObjects", "ExternalKeys", "ObjectObservations", "CollectionRuns", "SectionRuns", "RawArtifacts",
            "Populations", "PopulationMembers", "PopulationEntries", "ScopeDecisions", "ExportJobs", "ExportArtifacts", "TargetMappingRules",
            "CollectionJobs", "Environments"];
        expectedGrowth.Where(x => populated[x] <= baseline[x]).ShouldBeEmpty();
        var doomedFiles = artifacts.Names.Concat(exports.Names).Except(survivorFiles).ToList();
        doomedFiles.Count.ShouldBeGreaterThan(1);

        var deletion = new ProjectDeletionService(fixture.Factory, fixture.Access, fixture.Scope, exports);
        await Should.ThrowAsync<AccessDeniedException>(() => deletion.RequestAsync(project, "Synthetic audit"));
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Projects.Where(x => x.Id == project).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, ProjectStatus.Archived).SetProperty(x => x.ArchivedAtUtc, DateTimeOffset.UtcNow));
        }
        await Should.ThrowAsync<ArgumentException>(() => deletion.RequestAsync(project, "Synthetic"));
        await Should.ThrowAsync<AccessDeniedException>(() => new ProjectDeletionService(other.Factory, other.Access, other.Scope, exports)
            .RequestAsync(project, "Synthetic audit"));
        await deletion.RequestAsync(project, " Synthetic audit ");
        await deletion.RequestAsync(project, "Synthetic audit");
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.CollectionJobs.CountAsync(x => x.ProjectId == project && x.Kind == CollectionJobKind.Deletion)).ShouldBe(1);
            (await db.Projects.SingleAsync(x => x.Id == project)).Status.ShouldBe(ProjectStatus.Deleting);
        }
        await Should.ThrowAsync<AccessDeniedException>(() => fixture.Access.RequireAsync(project, WorkspaceOperation.Read));
        await Should.ThrowAsync<AccessDeniedException>(() => ScopeTests.Service(fixture).ApplyAsync(project,
            new(Guid.NewGuid(), ScopeValue.Exclude, null, "Late change", ObjectIds: [ids["carol"]]), 1));

        var clock = new ManualClock();
        var processor = new CollectionJobProcessor(new SqlJobQueue(fixture.ConnectionString, clock),
            [new DeletionJobHandler(artifacts, exports, clock)], NullLogger<CollectionJobProcessor>.Instance);
        artifacts.FailDeletes = true;
        (await processor.ProcessOneAsync(default)).ShouldBeTrue();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var job = await db.CollectionJobs.SingleAsync(x => x.ProjectId == project && x.Kind == CollectionJobKind.Deletion);
            (job.State, job.ErrorCode).ShouldBe((CollectionRunState.Pending, "StorageUnavailable"));
            (await db.Projects.SingleAsync(x => x.Id == project)).Status.ShouldBe(ProjectStatus.Deleting);
            (await db.ScopeDecisions.CountAsync(x => x.ProjectId == project)).ShouldBeGreaterThan(0);
        }
        artifacts.Names.ShouldContain(x => doomedFiles.Contains(x));

        artifacts.FailDeletes = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        (await processor.ProcessOneAsync(default)).ShouldBeTrue();
        (await processor.ProcessOneAsync(default)).ShouldBeFalse();

        var after = await CountsAsync(fixture.Factory);
        var expected = new Dictionary<string, long>(baseline, StringComparer.Ordinal)
        {
            ["Projects"] = baseline["Projects"] - 1,
            ["Environments"] = baseline["Environments"] - 1,
            ["AuditEvents"] = after["AuditEvents"]
        };
        after.ShouldBe(expected);
        after["AuditEvents"].ShouldBeGreaterThan(populated["AuditEvents"]);
        (await CountsAsync(other.Factory)).ShouldBe(otherBaseline);
        artifacts.Names.Concat(exports.Names).Order(StringComparer.Ordinal).ShouldBe(survivorFiles.Order(StringComparer.Ordinal));
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var actions = await db.AuditEvents.Where(x => x.ResourceId == project).Select(x => x.Action).ToListAsync();
            actions.ShouldContain("ProjectDeletionRequested");
            actions.ShouldContain("ProjectDeleted");
            (await db.Projects.SingleAsync(x => x.Id == survivor.Id)).Status.ShouldBe(ProjectStatus.Active);
        }
        (await ExportTests.Service(fixture, exports).PageAsync(survivor.Id, EnvironmentRole.Source, new())).Items.ShouldHaveSingleItem()
            .State.ShouldBe(CollectionRunState.Completed);
        await Should.ThrowAsync<AccessDeniedException>(() => fixture.Access.RequireAsync(project, WorkspaceOperation.DeleteProject));
    }

    [SqlFact]
    public async Task RetentionSweepPurgesDueFilesAndAuditKeepsEvidenceSkipsBusyOrDeletingProjectsAndIsIdempotent()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.WorkspaceManager));
        }
        var artifacts = new MemoryArtifactStore();
        var exports = new MemoryExportStore();
        var ws = fixture.Workspace.Id;
        var deleting = new Project { WorkspaceId = ws, Name = "Synthetic dossier being deleted" };
        var deletingEnvironment = new ProjectEnvironment
        {
            WorkspaceId = ws,
            ProjectId = deleting.Id,
            ExternalId = "forest.example.test",
            Kind = EnvironmentKind.AdForest
        };
        var archived = new Project { WorkspaceId = ws, Name = "Synthetic archived dossier" };
        archived.Archive();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.AddRange(deleting, deletingEnvironment, archived);
            db.AuditEvents.Add(new AuditEvent
            {
                WorkspaceId = ws,
                ResourceId = fixture.Project.Id,
                Action = "SyntheticOldEvent",
                OccurredAtUtc = DateTimeOffset.UtcNow.AddDays(-800),
                CorrelationId = "synthetic"
            });
            await db.SaveChangesAsync();
        }
        await UploadAsync(fixture, artifacts, deleting.Id, deletingEnvironment.Id, Package("deleting-user"), process: true);
        var processed = await UploadAsync(fixture, artifacts, fixture.Project.Id, fixture.Environment.Id, Package("processed-user"), process: true);
        var exportId = await ExportAsync(fixture, exports, fixture.Project.Id);
        var pending = await UploadAsync(fixture, artifacts, fixture.Project.Id, fixture.Environment.Id, Package("pending-user"), process: false);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Projects.Where(x => x.Id == deleting.Id).ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, ProjectStatus.Deleting));
        }
        var orphanRaw = (await artifacts.SaveAsync(ws, new MemoryStream([1, 2, 3]), 100, default)).BlobName;
        var orphanExport = (await exports.SaveAsync(ws, Guid.NewGuid(), ".csv", "text/csv", new MemoryStream([4, 5, 6]), default)).BlobName;
        string processedBlob, pendingBlob, deletingBlob;
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            processedBlob = await db.RawArtifacts.Where(x => x.RunId == processed).Select(x => x.BlobName).SingleAsync();
            pendingBlob = await db.RawArtifacts.Where(x => x.RunId == pending).Select(x => x.BlobName).SingleAsync();
            var deletingRuns = db.CollectionRuns.Where(x => x.ProjectId == deleting.Id).Select(x => x.Id);
            deletingBlob = await db.RawArtifacts.Where(x => deletingRuns.Contains(x.RunId)).Select(x => x.BlobName).SingleAsync();
        }
        var exportFiles = exports.Names.Where(x => x != orphanExport).ToList();
        exportFiles.ShouldNotBeEmpty();

        var clock = new ManualClock();
        clock.Advance(TimeSpan.FromDays(200));
        var sweeper = new RetentionSweeper(fixture.ConnectionString, artifacts, exports, new RetentionOptions(), clock,
            NullLogger<RetentionSweeper>.Instance);
        (await sweeper.RunOnceAsync(default)).ShouldBe(new RetentionSweep(1, 1, 1, 1, 2, 1, 0));

        artifacts.Names.Order(StringComparer.Ordinal).ShouldBe(new[] { pendingBlob, deletingBlob }.Order(StringComparer.Ordinal));
        exports.Names.ShouldBeEmpty();
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.RawArtifacts.SingleAsync(x => x.RunId == processed)).PurgedAtUtc.ShouldBe(clock.GetUtcNow());
            (await db.RawArtifacts.SingleAsync(x => x.RunId == pending)).PurgedAtUtc.ShouldBeNull();
            (await db.ExportJobs.SingleAsync(x => x.Id == exportId)).FilesPurgedAtUtc.ShouldBe(clock.GetUtcNow());
            (await db.ExportArtifacts.CountAsync(x => x.ExportJobId == exportId)).ShouldBeGreaterThan(0);
            (await db.InventoryObjects.CountAsync()).ShouldBeGreaterThan(0);
            (await db.AuditEvents.AnyAsync(x => x.Action == "SyntheticOldEvent")).ShouldBeFalse();
            var system = await db.AuditEvents.Where(x => x.ActorObjectId == Guid.Empty).Select(x => new { x.Action, x.ResourceId }).ToListAsync();
            system.Select(x => (x.Action, x.ResourceId)).Order().ShouldBe(new[]
            {
                ("AuditEventsPurged", (Guid?)ws), ("ExportFilesExpired", exportId), ("RawFilePurged", processed)
            }.Order());
        }
        var raw = await Should.ThrowAsync<FileExpiredException>(() => ZipImportTests.Service(fixture, artifacts).DownloadRawAsync(fixture.Project.Id, processed));
        raw.Code.ShouldBe("RawFilePurged");
        var exportService = ExportTests.Service(fixture, exports);
        var row = (await exportService.PageAsync(fixture.Project.Id, EnvironmentRole.Source, new())).Items.ShouldHaveSingleItem();
        row.FilesPurgedAtUtc.ShouldBe(clock.GetUtcNow());
        var expired = await Should.ThrowAsync<FileExpiredException>(() => exportService.DownloadAsync(fixture.Project.Id, exportId, row.Artifacts[0].Id));
        expired.Code.ShouldBe("ExportFilesExpired");
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            // Whatever the application asks, SQL refuses to delete audit events younger than 29 days.
            (await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() => db.AuditEvents.Where(x => x.ActorObjectId != Guid.Empty)
                .ExecuteDeleteAsync())).Message.ShouldContain("block predicate");
            (await db.AuditEvents.CountAsync(x => x.ActorObjectId != Guid.Empty)).ShouldBeGreaterThan(0);
        }

        (await sweeper.RunOnceAsync(default)).ShouldBe(new RetentionSweep(1, 0, 0, 0, 0, 1, 0));
    }

    private static DiscoveryPackage Package(string externalId)
    {
        var package = DiscoveryFixture.Package();
        return package with { Objects = [package.Objects[0] with { ExternalId = externalId }] };
    }

    private static async Task<Guid> UploadAsync(DiscoveryFixture fixture, MemoryArtifactStore store, Guid project, Guid environment,
        DiscoveryPackage package, bool process)
    {
        var imports = ZipImportTests.Service(fixture, store);
        var run = await imports.UploadAsync(project, environment, new MemoryStream(await ZipImportTests.BytesAsync(package)), "dossier.zip");
        await imports.ConfirmAsync(project, run);
        if (process) { (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue(); }
        return run;
    }

    private static async Task<Guid> ExportAsync(DiscoveryFixture fixture, MemoryExportStore store, Guid project)
    {
        var id = await ExportTests.Service(fixture, store).LaunchAsync(project, new(Guid.NewGuid(), new(EnvironmentRole.Source)));
        (await ExportTests.ProcessAsync(fixture, store)).ShouldBeTrue();
        (await ExportTests.Service(fixture, store).PageAsync(project, EnvironmentRole.Source, new())).Items.Single(x => x.Id == id)
            .State.ShouldBe(CollectionRunState.Completed);
        return id;
    }

    /// <summary>Row counts of every workspace-owned table as the factory's SQL session (and therefore row-level security) sees them.</summary>
    private static async Task<Dictionary<string, long>> CountsAsync(IDbContextFactory<ClarityDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        var tables = new List<string>();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                SELECT t.name FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id
                WHERE c.name = 'WorkspaceId' AND SCHEMA_NAME(t.schema_id) = 'dbo'
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { tables.Add(reader.GetString(0)); }
        }
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            counts[table] = await ScalarAsync(db, $"SELECT COUNT_BIG(*) FROM [dbo].[{table}]");
        }
        return counts;
    }

    private static async Task<long> ScalarAsync(ClarityDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(1);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
