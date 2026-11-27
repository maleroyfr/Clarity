using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

/// <summary>
/// Large-volume timing on a disposable SQL Server (docs/resilience-performance.md). One AD forest is collected as five domain shards of
/// <c>CLARITY_TEST_VOLUME</c> synthetic users each (10,000 per shard give 50,000 users and 500,020 memberships), followed by the matching
/// Entra tenant. Every step a consultant triggers is then timed into the report at <c>CLARITY_VOLUME_REPORT</c>; interactive reads must stay
/// within <see cref="InteractiveBudget"/> and batch operations within <see cref="BatchBudget"/>.
/// </summary>
[Collection("SQL lifecycle")]
public sealed class VolumeTests
{
    private const int Shards = 5;
    private const int ProjectMemberships = 9;
    private static readonly Guid Tenant = Guid.Parse("0f1e2d3c-4b5a-4968-8776-655443322110");
    private static readonly TimeSpan InteractiveBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BatchBudget = TimeSpan.FromMinutes(3);

    private static string Domain(int shard) => $"d{shard}.example.test";

    [VolumeFact]
    [Trait("Category", "Volume")]
    public async Task ShardedForestAndTenantStayWithinPageAndBatchBudgets()
    {
        var perShard = int.Parse(Environment.GetEnvironmentVariable("CLARITY_TEST_VOLUME")!, CultureInfo.InvariantCulture);
        // A shard's domain population (users plus groups) must fit ScopeRules.MaxPopulation and all users must fit one export.
        perShard.ShouldBeInRange(25, ExportRules.MaxRows / Shards);
        var shards = Enumerable.Range(1, Shards)
            .Select(s => new SyntheticDossier(perShard, ProjectMemberships, seed: 100 + s, domain: Domain(s))).ToArray();
        var users = perShard * Shards;
        var memberships = shards.Sum(s => s.Relationships);
        var groups = shards.Sum(s => s.Groups);
        var report = new VolumeReport(Environment.GetEnvironmentVariable("CLARITY_VOLUME_REPORT"));
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var project = fixture.Project.Id;
        var ad = fixture.Environment.Id;
        var store = new MemoryArtifactStore();
        report.Header($"{users:N0} AD users, {groups:N0} groups and {memberships:N0} memberships in {Shards} domain shards; "
            + $"{users:N0} Entra users; {Environment.ProcessorCount} processors, {RuntimeInformation.OSDescription}; "
            + $"SQL Server {await ScalarAsync(fixture, $"SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS [Value]")}");

        using var stages = new PublicationStages();
        for (var s = 0; s < Shards; s++)
        {
            var package = shards[s].AdForest();
            await ImportAsync($"Import AD shard {s + 1} ({package.Objects.Count:N0} objects, {package.Relationships.Count:N0} memberships)",
                ad, package);
        }
        var cloud = await AddTenantAsync(fixture);
        var tenant = TenantPackage(shards);
        await ImportAsync($"Import Entra tenant ({tenant.Objects.Count:N0} objects) and match hybrid identities", cloud, tenant);
        var keys = await KeysAsync(fixture, ad);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.InventoryObjects.CountAsync(o => o.EnvironmentId == ad && o.Type == "user")).ShouldBe(users);
            (await db.InventoryObjects.CountAsync(o => o.EnvironmentId == cloud && o.Type == "user")).ShouldBe(users);
            (await db.InventoryRelationships.CountAsync(r => r.EnvironmentId == ad && r.IsCurrent && r.FromObjectId != null && r.ToObjectId != null))
                .ShouldBe(memberships);
        }
        var objects = users * 2 + groups + Shards;

        var inventory = new InventoryQueries(fixture.Factory, fixture.Access);
        var source = new InventoryFilter(EnvironmentRole.Source);
        PageRequest Page(int start = 0, string search = "", string? sort = null, bool descending = false) => new(start, 100, search, descending, sort);
        (await report.ReadAsync("Inventory grid, first page by name", () => inventory.PageAsync(project, source, Page()))).TotalCount.ShouldBe(objects);
        (await report.ReadAsync("Inventory grid, last page by name", () => inventory.PageAsync(project, source, Page(objects - 100)))).Items.Count
            .ShouldBe(100);
        await report.ReadAsync("Inventory grid, descending type", () => inventory.PageAsync(project, source, Page(sort: nameof(InventoryRow.Type), descending: true)));
        await report.ReadAsync("Inventory grid, by last observation", () => inventory.PageAsync(project, source, Page(sort: nameof(InventoryRow.LastObservedAtUtc))));
        (await report.ReadAsync("Inventory grid, disabled users", () => inventory.PageAsync(project, source with { Type = "user", Status = "Disabled" }, Page())))
            .TotalCount.ShouldBe(perShard / 7 * Shards);
        (await report.ReadAsync("Inventory grid, AD environment", () => inventory.PageAsync(project, source with { Environment = ad }, Page())))
            .TotalCount.ShouldBe(users + groups);
        (await report.ReadAsync("Inventory grid, one domain", () => inventory.PageAsync(project, source with { Domain = Domain(3) }, Page())))
            .TotalCount.ShouldBe(perShard * 2 + shards[2].Groups);
        (await report.ReadAsync("Inventory grid, name prefix search", () => inventory.PageAsync(project, source, Page(search: "Person 0001"))))
            .TotalCount.ShouldBeGreaterThan(0);
        (await report.ReadAsync("Inventory grid, UPN prefix search", () => inventory.PageAsync(project, source, Page(search: $"u00010@{Domain(4)}"))))
            .TotalCount.ShouldBe(2);
        (await report.ReadAsync("Inventory grid, conflicts only", () => inventory.PageAsync(project, source with { ConflictsOnly = true }, Page())))
            .TotalCount.ShouldBe(0);
        await report.ReadAsync("Inventory grid, unresolved links", () => inventory.PageAsync(project, source with { Unresolved = true }, Page()));
        await report.ReadAsync("Inventory filter options", () => inventory.OptionsAsync(project, EnvironmentRole.Source));
        await report.ReadAsync("Inventory overview", () => new InventoryOverview(fixture.Factory, fixture.Access).ReadAsync(project, EnvironmentRole.Source));

        var sheet = new InventorySheet(fixture.Factory, fixture.Access);
        var user = keys[shards[2].UserId(1)];
        var department = keys[shards[0].GroupId(SyntheticDossier.Departments[0])];
        var allStaff = keys[shards[0].GroupId(SyntheticDossier.AllStaff)];
        await report.ReadAsync("Object sheet", () => sheet.ReadAsync(project, user));
        (await report.ReadAsync("Object sheet, user relationships", () => sheet.EdgesAsync(project, user, Page()))).TotalCount
            .ShouldBe(1 + ProjectMemberships);
        (await report.ReadAsync("Object sheet, department group members", () => sheet.EdgesAsync(project, department, Page()))).TotalCount
            .ShouldBe((perShard + 3) / 4 + 1);
        await report.ReadAsync("Object sheet, observation history", () => sheet.HistoryAsync(project, user, Page()));
        await report.ReadAsync("Object sheet, alternate keys", () => sheet.AliasesAsync(project, user, Page()));
        var graph = new InventoryGraph(fixture.Factory, fixture.Access);
        (await report.ReadAsync("Pending links", () => graph.PendingAsync(project, EnvironmentRole.Source, Page()))).TotalCount.ShouldBe(0);
        (await report.ReadAsync("Effective memberships of a user", () => graph.MembershipsAsync(project, user, reverse: false))).Count
            .ShouldBe(2 + ProjectMemberships);
        (await report.ReadAsync("Transitive members of all-staff", () => graph.MembershipsAsync(project, allStaff, reverse: true))).Count
            .ShouldBe(perShard + SyntheticDossier.Departments.Length);

        var mappings = new MappingReview(fixture.Factory, fixture.Access, fixture.Scope);
        (await report.ReadAsync("Hybrid mapping grid, first page", () => mappings.PageAsync(project, EnvironmentRole.Source, "All", Page()))).TotalCount
            .ShouldBe(users);
        await report.ReadAsync("Hybrid mapping grid, last page", () => mappings.PageAsync(project, EnvironmentRole.Source, "All", Page(users - 100)));
        await report.ReadAsync("Hybrid mapping grid, review queue", () => mappings.PageAsync(project, EnvironmentRole.Source, "Queue", Page()));

        var scope = ScopeTests.Service(fixture);
        var population = await report.TimeAsync("Create a domain population", BatchBudget, () => scope.CreatePopulationAsync(project,
            new(Guid.NewGuid(), ad, "Domaine d1", PopulationKind.Domain, Domain(1))));
        (await report.ReadAsync("Population detail", () => scope.GetPopulationAsync(project, population))).MemberCount
            .ShouldBe(perShard + shards[0].Groups);
        await report.ReadAsync("Population members, first page", () => scope.MembersAsync(project, population, Page()));
        var batch = new ScopeBatch(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Synthetic volume wave", population);
        var preview = await report.TimeAsync("Preview including the population", BatchBudget, () => scope.PreviewAsync(project, batch));
        preview.Affected.ShouldBe(perShard + shards[0].Groups);
        await report.TimeAsync("Apply the scope decisions", BatchBudget, () => scope.ApplyAsync(project, batch, preview.Affected));
        (await report.ReadAsync("Scope decisions grid", () => scope.DecisionsAsync(project, EnvironmentRole.Source, "Include", Page()))).TotalCount
            .ShouldBe(preview.Affected);
        await report.ReadAsync("Scope summary", () => scope.SummaryAsync(project, EnvironmentRole.Source));
        var register = new DecisionRegisterService(fixture.Factory, fixture.Access, fixture.Scope);
        await report.ReadAsync("Decision register summary", () => register.SummaryAsync(project, EnvironmentRole.Source));
        (await report.ReadAsync("Decision register, scope page", () => register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, false, Page())))
            .TotalCount.ShouldBe(preview.Affected);
        await report.TimeAsync("Recompute decision review", BatchBudget, () => register.RecomputeAsync(project));
        await report.ReadAsync("Scenario overview", () => new ScenarioOverviewService(fixture.Factory, fixture.Access).ReadAsync(project));

        var exportStore = new MemoryExportStore();
        var exports = ExportTests.Service(fixture, exportStore);
        await exports.LaunchAsync(project, new(Guid.NewGuid(), new(EnvironmentRole.Source, ad, Type: "user")));
        (await report.TimeAsync($"Generate an export of {users:N0} users", BatchBudget, () => ExportTests.ProcessAsync(fixture, exportStore))).ShouldBeTrue();
        var export = (await report.ReadAsync("Export list", () => exports.PageAsync(project, EnvironmentRole.Source, new()))).Items.ShouldHaveSingleItem();
        (export.State, export.ObjectCount, export.Artifacts.Count).ShouldBe((CollectionRunState.Completed, (int?)users, 3));
        report.Note($"Export files: {string.Join(", ", export.Artifacts.Select(a => $"{a.Kind} {a.Bytes:N0} bytes"))}");
        if (objects > ExportRules.MaxRows)
        {
            await exports.LaunchAsync(project, new(Guid.NewGuid(), new(EnvironmentRole.Source)));
            (await ExportTests.ProcessAsync(fixture, exportStore)).ShouldBeTrue();
            var refused = (await exports.PageAsync(project, EnvironmentRole.Source, new())).Items.First(x => x.Id != export.Id);
            (refused.State, refused.Artifacts.Count).ShouldBe((CollectionRunState.Failed, 0));
        }

        report.Write();
        report.OverBudget.ShouldBeEmpty();

        async Task ImportAsync(string step, Guid environment, DiscoveryPackage package)
        {
            using (stages.Begin())
            {
                await report.TimeAsync(step, null, () => UploadAsync(fixture, store, environment, package));
            }
            foreach (var (stage, elapsed) in stages.Drain()) { report.Detail(stage, elapsed); }
        }
    }

    private static DiscoveryPackage TenantPackage(IReadOnlyList<SyntheticDossier> shards)
    {
        var parts = shards.Select((s, i) => s.Entra(Tenant, Domain(i + 1))).ToList();
        var users = parts.SelectMany(p => p.Objects.Where(o => o.SectionId == "entra.users")).ToList();
        var domains = parts.SelectMany(p => p.Objects.Where(o => o.SectionId == "entra.domains")).ToList();
        var first = parts[0];
        return first with
        {
            Objects = [.. users, .. domains],
            Results = [first.Results[0] with { ObjectCount = users.Count }, first.Results[1] with { ObjectCount = domains.Count }]
        };
    }

    private static async Task UploadAsync(DiscoveryFixture fixture, MemoryArtifactStore store, Guid environment, DiscoveryPackage package)
    {
        var imports = ZipImportTests.Service(fixture, store);
        var run = await imports.UploadAsync(fixture.Project.Id, environment, new MemoryStream(await ZipImportTests.BytesAsync(package)), "volume.zip");
        await imports.ConfirmAsync(fixture.Project.Id, run);
        (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
        await using var db = await fixture.Factory.CreateDbContextAsync();
        var job = await db.CollectionJobs.AsNoTracking().OrderByDescending(j => j.CreatedAtUtc).Select(j => new { j.State, j.ErrorCode }).FirstAsync();
        job.State.ShouldBe(CollectionRunState.Completed, $"Import job ended {job.State} with {job.ErrorCode}.");
    }

    private static async Task<Dictionary<string, Guid>> KeysAsync(DiscoveryFixture fixture, Guid environment)
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();
        return await db.ExternalKeys.AsNoTracking().Where(k => k.IsPrimary && k.EnvironmentId == environment)
            .ToDictionaryAsync(k => k.ExternalId, k => k.ObjectId, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Guid> AddTenantAsync(DiscoveryFixture fixture)
    {
        var environment = new ProjectEnvironment
        {
            WorkspaceId = fixture.Workspace.Id,
            ProjectId = fixture.Project.Id,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = Tenant.ToString(),
            Role = EnvironmentRole.Source
        };
        await using var db = await fixture.Factory.CreateDbContextAsync();
        db.Environments.Add(environment);
        await db.SaveChangesAsync();
        return environment.Id;
    }

    private static async Task<string> ScalarAsync(DiscoveryFixture fixture, FormattableString sql)
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();
        return await db.Database.SqlQuery<string>(sql).SingleAsync();
    }

    /// <summary>Totals the publication stage activities started under the current import, ignoring publications of concurrent tests.</summary>
    private sealed class PublicationStages : IDisposable
    {
        private const string SourceName = "Clarity.Volume";
        private static readonly ActivitySource Source = new(SourceName);
        private readonly ConcurrentDictionary<string, TimeSpan> totals = new(StringComparer.Ordinal);
        private readonly ActivityListener listener;
        private Activity? root;

        public PublicationStages()
        {
            listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name is SourceName or "Clarity.Publication",
                Sample = (ref _) => ActivitySamplingResult.AllData,
                ActivityStopped = Stopped
            };
            ActivitySource.AddActivityListener(listener);
        }

        public IDisposable Begin()
        {
            root = Source.StartActivity("Import");
            return root ?? throw new InvalidOperationException("The volume activity was not sampled.");
        }

        public IEnumerable<(string Stage, TimeSpan Elapsed)> Drain()
        {
            var result = totals.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, x.Value)).ToList();
            totals.Clear();
            return result;
        }

        public void Dispose() => listener.Dispose();

        private void Stopped(Activity activity)
        {
            if (activity.Source != PublicationService.Activities) { return; }
            for (var parent = activity.Parent; parent is not null; parent = parent.Parent)
            {
                if (ReferenceEquals(parent, root))
                {
                    totals.AddOrUpdate(activity.OperationName, activity.Duration, (_, total) => total + activity.Duration);
                    return;
                }
            }
        }
    }

    /// <summary>Timings appended as Markdown table rows, so a run that stops on a timeout still reports the steps it completed.</summary>
    private sealed class VolumeReport(string? path)
    {
        private readonly List<(string Step, TimeSpan Elapsed, TimeSpan? Budget)> steps = [];

        public IEnumerable<string> OverBudget => steps.Where(s => s.Elapsed > s.Budget).Select(s => $"{s.Step}: {s.Elapsed.TotalSeconds:F1} s");

        public void Header(string context)
        {
            Append($"# Clarity volume run {DateTimeOffset.UtcNow:u}\n\n{context}\n\n| Step | Milliseconds | Budget |\n| --- | ---: | ---: |");
        }

        public void Note(string text) => Append($"| {text} | | |");

        public void Detail(string stage, TimeSpan elapsed) => Append(FormattableString.Invariant($"| - {stage} | {elapsed.TotalMilliseconds:F0} | |"));

        public Task<T> ReadAsync<T>(string step, Func<Task<T>> action) => TimeAsync(step, InteractiveBudget, action);

        public async Task<T> TimeAsync<T>(string step, TimeSpan? budget, Func<Task<T>> action)
        {
            var watch = Stopwatch.StartNew();
            var result = await action();
            Record(step, watch.Elapsed, budget);
            return result;
        }

        public async Task TimeAsync(string step, TimeSpan? budget, Func<Task> action)
        {
            var watch = Stopwatch.StartNew();
            await action();
            Record(step, watch.Elapsed, budget);
        }

        public void Write()
        {
            var over = OverBudget.ToList();
            Append(over.Count == 0 ? "\nAll budgeted steps are within budget." : $"\nOver budget: {string.Join("; ", over)}.");
        }

        private void Record(string step, TimeSpan elapsed, TimeSpan? budget)
        {
            steps.Add((step, elapsed, budget));
            Append(FormattableString.Invariant($"| {step} | {elapsed.TotalMilliseconds:F0} | {(budget is { } b ? $"{b.TotalMilliseconds:F0}" : "")} |"));
        }

        private void Append(string text)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(text);
            if (!string.IsNullOrWhiteSpace(path)) { File.AppendAllText(path, text + "\n"); }
        }
    }
}
