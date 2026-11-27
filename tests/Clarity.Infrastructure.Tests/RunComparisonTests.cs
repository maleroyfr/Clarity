using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class RunComparisonTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000321");
    private static readonly CollectionScope Scope = new("tenant", Tenant.ToString());
    private static readonly DateTimeOffset First = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Second = First.AddDays(14);
    private const string Alice = "d1000000-0000-0000-0000-000000000001", Bob = "d1000000-0000-0000-0000-000000000002";
    private const string TeamA = "e1000000-0000-0000-0000-00000000000a", TeamB = "e1000000-0000-0000-0000-00000000000b";
    private const string TeamC = "e1000000-0000-0000-0000-00000000000c";
    private const string SiteA = "8b3e4c30-3333-4c0d-9e33-00000000000a", SiteB = "8b3e4c30-3333-4c0d-9e33-00000000000b";

    [SqlFact]
    public async Task ExhaustiveTeamsSectionsProveChangesWhilePartialAndUserSectionsNeverInventRemovals()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var (environment, runs, ids) = await SeedAsync(fixture);
        var project = fixture.Project.Id;
        var scoping = ScopeTests.Service(fixture);
        await scoping.ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Synthetic migrating user", ObjectIds: [ids["alice"]]), 1);
        await scoping.ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.Exclude, null, "Synthetic retired team", ObjectIds: [ids["teamB"]]), 1);
        var service = Service(fixture);

        (await service.RunsAsync(project, environment.Id)).Select(r => r.Id).ShouldBe(runs.Values, ignoreOrder: true);
        var operation = Guid.NewGuid();
        var teams = await service.CompareAsync(project, environment.Id, runs["teams1"], runs["teams2"], operation);
        (await service.CompareAsync(project, environment.Id, runs["teams1"], runs["teams2"], operation)).ShouldBe(teams);
        await Should.ThrowAsync<ConflictException>(() => service.CompareAsync(project, environment.Id, runs["entra1"], runs["entra2"], operation));
        await Should.ThrowAsync<ArgumentException>(() => service.CompareAsync(project, environment.Id, runs["teams2"], runs["teams1"], Guid.NewGuid()));
        await Should.ThrowAsync<ArgumentException>(() => service.CompareAsync(project, environment.Id, runs["teams1"], runs["teams1"], Guid.NewGuid()));

        var summary = (await service.ComparisonsAsync(project, EnvironmentRole.Source)).Single();
        (summary.Added, summary.Modified, summary.Removed, summary.NewlyObserved, summary.NotObserved, summary.Unchanged).ShouldBe((1, 1, 1, 0, 1, 1));
        (summary.RelationsAdded, summary.RelationsRemoved, summary.RelationsUnknown).ShouldBe((1, 1, 1));
        summary.Units.Select(u => (u.CapabilityId, u.Comparison))
            .ShouldBe([("teams.sites", UnitComparison.NotExhaustive), ("teams.teams", UnitComparison.Comparable)], ignoreOrder: true);

        var items = (await service.ItemsAsync(project, teams, null, false, new())).Items.ToDictionary(i => i.ObjectId);
        items[ids["teamA"]].ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.Modified),
            i => i.Changes.ShouldBe([new FieldChange("visibility", "private", "public")]), i => i.HasAffectedDecision.ShouldBeFalse());
        items[ids["teamB"]].ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.Removed), i => i.ScopeReview.ShouldBeTrue(),
            i => i.HasAffectedDecision.ShouldBeTrue());
        items[ids["teamC"]].Change.ShouldBe(RunChange.Added);
        items[ids[SiteB]].ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.NotObserved), i => i.ScopeReview.ShouldBeFalse());
        items.ShouldNotContainKey(ids[SiteA]);
        (await service.ItemsAsync(project, teams, RunChange.Removed, false, new())).Items.Single().ObjectId.ShouldBe(ids["teamB"]);
        (await service.ItemsAsync(project, teams, null, true, new())).Items.Single().ObjectId.ShouldBe(ids["teamB"]);
        (await service.DecisionsAsync(project, teams, new())).Items.Single()
            .ShouldBe(new ComparisonDecisionRow(DecisionKind.Scope, ids["teamB"], "Legacy", "team", RunChange.Removed, "", "ScopeDecision_Exclude", null));

        var users = await service.CompareAsync(project, environment.Id, runs["entra1"], runs["entra2"], Guid.NewGuid());
        var userItems = (await service.ItemsAsync(project, users, null, false, new())).Items.ToDictionary(i => i.ObjectId);
        userItems[ids["alice"]].ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.Modified), i => i.ChangedFields.ShouldBe("userPrincipalName"),
            i => i.ScopeReview.ShouldBeTrue(), i => i.MappingReview.ShouldBeTrue(), i => i.HasAffectedDecision.ShouldBeTrue());
        userItems[ids["bob"]].ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.NotObserved), i => i.ScopeReview.ShouldBeFalse());
        var decision = (await service.DecisionsAsync(project, users, new())).Items.Single();
        (decision.Kind, decision.ObjectId, decision.Label, decision.Detail).ShouldBe((DecisionKind.Scope, ids["alice"], "ScopeDecision_Include", "Vague 1"));

        var replaced = await service.CompareAsync(project, environment.Id, runs["teams1"], runs["teams2"], Guid.NewGuid());
        (await service.ComparisonsAsync(project, EnvironmentRole.Source)).Select(c => c.Id).ShouldBe([replaced, users], ignoreOrder: true);
        await Should.ThrowAsync<AccessDeniedException>(() => service.ItemsAsync(project, teams, null, false, new()));
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.AuditEvents.CountAsync(a => a.Action == "Comparison.RunsCompared")).ShouldBe(3);
        (await db.RunComparisonItems.CountAsync(i => i.ComparisonId == teams)).ShouldBe(0);
        (await db.ScopeDecisions.CountAsync()).ShouldBe(2);
    }

    [SqlFact]
    public async Task ComparisonsAreWorkspaceIsolatedAndReadersCannotCompare()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var (environment, runs, _) = await SeedAsync(fixture);
        var project = fixture.Project.Id;
        var service = Service(fixture);
        var id = await service.CompareAsync(project, environment.Id, runs["teams1"], runs["teams2"], Guid.NewGuid());

        var other = await fixture.OtherWorkspaceAsync();
        var foreign = new RunComparisonService(other.Factory, other.Access, other.Scope, DiscoveryCatalog.Load());
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.RunsAsync(project, environment.Id));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.ComparisonsAsync(project, EnvironmentRole.Source));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.ItemsAsync(other.Project.Id, id, null, false, new()));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.DecisionsAsync(other.Project.Id, id, new()));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.CompareAsync(project, environment.Id, runs["teams1"], runs["teams2"], Guid.NewGuid()));
        await Should.ThrowAsync<AccessDeniedException>(() =>
            foreign.CompareAsync(other.Project.Id, environment.Id, runs["teams1"], runs["teams2"], Guid.NewGuid()));

        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        }
        (await service.ComparisonsAsync(project, EnvironmentRole.Source)).Single().Id.ShouldBe(id);
        (await service.ItemsAsync(project, id, null, false, new())).TotalCount.ShouldBe(4);
        await Should.ThrowAsync<AccessDeniedException>(() => service.CompareAsync(project, environment.Id, runs["teams1"], runs["teams2"], Guid.NewGuid()));

        await using var outside = PersistenceTests.Create(fixture.ConnectionString, []);
        (await outside.RunComparisons.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.RunComparisonItems.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    private static RunComparisonService Service(DiscoveryFixture fixture) => new(fixture.Factory, fixture.Access, fixture.Scope, DiscoveryCatalog.Load());

    /// <summary>
    /// Publishes two synthetic collections per producer. Teams: A changes visibility, B disappears from an exhaustive team list, C appears,
    /// and the later site section is partial so site B is only not observed. Entra: Alice changes UPN and Bob is missing from a user list that
    /// never proves absence.
    /// </summary>
    private static async Task<(ProjectEnvironment Environment, Dictionary<string, Guid> Runs, Dictionary<string, Guid> Ids)> SeedAsync(
        DiscoveryFixture fixture)
    {
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.WorkspaceManager));
        }
        var environment = new ProjectEnvironment
        {
            WorkspaceId = fixture.Workspace.Id,
            ProjectId = fixture.Project.Id,
            Kind = EnvironmentKind.M365Tenant,
            ExternalId = Tenant.ToString()
        };
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.Environments.Add(environment);
            await db.SaveChangesAsync();
        }
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        var runs = new Dictionary<string, Guid>();
        foreach (var (name, package) in Packages())
        {
            var run = await imports.UploadAsync(fixture.Project.Id, environment.Id, new MemoryStream(await ZipImportTests.BytesAsync(package)), "package.zip");
            await imports.ConfirmAsync(fixture.Project.Id, run);
            (await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default)).ShouldBeTrue();
            runs[name] = run;
        }
        await using var check = await fixture.Factory.CreateDbContextAsync();
        (await check.CollectionRuns.CountAsync(r => r.State == CollectionRunState.Completed)).ShouldBe(4);
        var keys = await check.ExternalKeys.AsNoTracking().Where(k => k.EnvironmentId == environment.Id && k.IsPrimary).ToListAsync();
        Guid Key(string type, string id) => keys.Single(k => k.Type == type && k.ExternalId == id).ObjectId;
        return (environment, runs, new()
        {
            ["alice"] = Key("user", Alice),
            ["bob"] = Key("user", Bob),
            ["teamA"] = Key("team", TeamA),
            ["teamB"] = Key("team", TeamB),
            ["teamC"] = Key("team", TeamC),
            [SiteA] = Key("site", SiteA),
            [SiteB] = Key("site", SiteB)
        });
    }

    private static (string Name, DiscoveryPackage Package)[] Packages()
    {
        static Dictionary<string, JsonElement> Attributes(params (string Name, object Value)[] values) =>
            values.ToDictionary(x => x.Name, x => JsonSerializer.SerializeToElement(x.Value));
        static ExternalReference Ref(string type, string ns, string id) => new(type, ns, id);
        static PackageManifest Manifest(string producer, DateTimeOffset date) => new("1.0", Guid.NewGuid(), producer, "1.0.0", "M365Tenant",
            Tenant.ToString(), Scope, date, date, new Dictionary<string, string>(), "Synthetic comparison");
        static ObjectRecord User(string id, string upn, DateTimeOffset date) => new("entra.users", "user", "microsoft.graph", id, date,
            Attributes(("displayName", id == Alice ? "Alice" : "Bob"), ("userPrincipalName", upn)));
        static DiscoveryPackage Entra(DateTimeOffset date, params ObjectRecord[] users) => new(Manifest("clarity.graph.entra", date), users, [],
            [new SectionResult("entra.users", "entra.users", CoverageStatus.Complete, Scope, ["displayName", "userPrincipalName"], [], users.Length, 0, [], [])]);
        static ObjectRecord Team(string id, string name, string visibility, DateTimeOffset date) => new("teams.teams", "team", "microsoft.graph", id, date,
            Attributes(("displayName", name), ("visibility", visibility)));
        static RelationshipRecord TeamGroup(string id, DateTimeOffset date) =>
            new("teams.teams", "teamGroup", Ref("team", "microsoft.graph", id), Ref("group", "microsoft.graph", id), date);
        static ObjectRecord Site(string id, DateTimeOffset date) => new("teams.sites", "site", "sharepoint.siteId", id, date,
            Attributes(("webUrl", "https://contoso.sharepoint.com/sites/" + id[^1]), ("teamsConnected", true)));
        static RelationshipRecord TeamSite(string team, string site, DateTimeOffset date) =>
            new("teams.sites", "teamSite", Ref("team", "microsoft.graph", team), Ref("site", "sharepoint.siteId", site), date);

        var teams1 = new DiscoveryPackage(Manifest("clarity.graph.teams", First),
            [Team(TeamA, "Project", "private", First), Team(TeamB, "Legacy", "public", First), Site(SiteA, First), Site(SiteB, First)],
            [TeamGroup(TeamA, First), TeamGroup(TeamB, First), TeamSite(TeamA, SiteA, First), TeamSite(TeamB, SiteB, First)],
            [
                new SectionResult("teams.teams", "teams.teams", CoverageStatus.Complete, Scope, ["displayName", "visibility"], ["teamGroup"], 2, 2, [], []),
                new SectionResult("teams.sites", "teams.sites", CoverageStatus.Complete, Scope, ["webUrl", "teamsConnected"],
                    ["groupSite", "teamSite", "channelSite"], 2, 2, [], [])
            ]);
        var teams2 = new DiscoveryPackage(Manifest("clarity.graph.teams", Second),
            [Team(TeamA, "Project", "public", Second), Team(TeamC, "New", "private", Second), Site(SiteA, Second)],
            [TeamGroup(TeamA, Second), TeamGroup(TeamC, Second), TeamSite(TeamA, SiteA, Second)],
            [
                new SectionResult("teams.teams", "teams.teams", CoverageStatus.Complete, Scope, ["displayName", "visibility"], ["teamGroup"], 2, 2, [], []),
                new SectionResult("teams.sites", "teams.sites", CoverageStatus.Partial, Scope, ["webUrl", "teamsConnected"],
                    ["groupSite", "teamSite", "channelSite"], 1, 1, [new SectionIssue("Limited", "One team site was unreadable.")], [])
            ]);
        var packages = new (string, DiscoveryPackage)[]
        {
            ("entra1", Entra(First, User(Alice, "alice@contoso.example", First), User(Bob, "bob@contoso.example", First))),
            ("teams1", teams1),
            ("entra2", Entra(Second, User(Alice, "alice.martin@contoso.example", Second))),
            ("teams2", teams2)
        };
        foreach (var (_, package) in packages)
        {
            DiscoveryCatalog.Load().ValidatePackage(package);
            new PackageValidator().Validate(package);
        }
        return packages;
    }
}
