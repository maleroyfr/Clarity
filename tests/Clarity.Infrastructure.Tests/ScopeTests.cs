using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class ScopeTests
{
    private const string Sales = "OU=Sales,DC=example,DC=test";

    [SqlFact]
    public async Task PopulationsResolveGroupsOrganizationalUnitsDomainsFiltersAndImportedLists()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var ids = await PublishAsync(fixture);
        var scope = Service(fixture);
        var env = fixture.Environment.Id;

        var group = await scope.CreatePopulationAsync(fixture.Project.Id, new(Guid.NewGuid(), env, "Sales group", PopulationKind.Group,
            RootObjectId: ids["sales"]));
        (await scope.GetPopulationAsync(fixture.Project.Id, group)).MemberCount.ShouldBe(3);
        (await scope.MembersAsync(fixture.Project.Id, group, new())).Items.Select(x => x.ObjectId)
            .ShouldBe([ids["alice"], ids["bob"], ids["nested"]], ignoreOrder: true);
        var users = await scope.CreatePopulationAsync(fixture.Project.Id, new(Guid.NewGuid(), env, "Sales users", PopulationKind.Group,
            RootObjectId: ids["sales"], ObjectType: "user"));
        (await scope.GetPopulationAsync(fixture.Project.Id, users)).MemberCount.ShouldBe(2);
        var ou = await scope.CreatePopulationAsync(fixture.Project.Id, new(Guid.NewGuid(), env, "Sales OU", PopulationKind.OrganizationalUnit,
            Sales.ToLowerInvariant()));
        (await scope.MembersAsync(fixture.Project.Id, ou, new())).Items.Select(x => x.ObjectId).ShouldBe([ids["alice"], ids["bob"]], ignoreOrder: true);
        var domain = await scope.CreatePopulationAsync(fixture.Project.Id, new(Guid.NewGuid(), env, "Enabled users", PopulationKind.Domain,
            "example.test", ObjectType: "user", Status: "Enabled"));
        (await scope.GetPopulationAsync(fixture.Project.Id, domain)).MemberCount.ShouldBe(4);
        var filter = await scope.CreatePopulationAsync(fixture.Project.Id, new(Guid.NewGuid(), env, "Carol", PopulationKind.Filter, "carol"));
        (await scope.MembersAsync(fixture.Project.Id, filter, new())).Items.Single().ObjectId.ShouldBe(ids["carol"]);
        await Should.ThrowAsync<ArgumentException>(() => scope.CreatePopulationAsync(fixture.Project.Id,
            new(Guid.NewGuid(), env, "Incomplete", PopulationKind.OrganizationalUnit, "Sales")));

        var replay = new PopulationInput(Guid.NewGuid(), env, "Replay", PopulationKind.Domain, "example.test");
        var first = await scope.CreatePopulationAsync(fixture.Project.Id, replay);
        (await scope.CreatePopulationAsync(fixture.Project.Id, replay)).ShouldBe(first);
        await Should.ThrowAsync<ConflictException>(() => scope.CreatePopulationAsync(fixture.Project.Id, replay with { Name = "Changed" }));

        var upn = await scope.ImportAsync(fixture.Project.Id, new(Guid.NewGuid(), env, "UPN list", ImportIdentifier.UserPrincipalName, null,
            "alice@example.test\r\nALICE@EXAMPLE.TEST\nghost@example.test\n\n"));
        var upnEntries = (await scope.EntriesAsync(fixture.Project.Id, upn, "All", new())).Items;
        upnEntries.Select(x => x.Resolution).ShouldBe([ImportResolution.Matched, ImportResolution.Unmatched]);
        upnEntries[0].ObjectId.ShouldBe(ids["alice"]);
        (await scope.GetPopulationAsync(fixture.Project.Id, upn)).Unresolved.ShouldBe(1);
        var sam = await scope.ImportAsync(fixture.Project.Id, new(Guid.NewGuid(), env, "Accounts", ImportIdentifier.SamAccountName, "user", "EXAMPLE\\bob"));
        (await scope.MembersAsync(fixture.Project.Id, sam, new())).Items.Single().ObjectId.ShouldBe(ids["bob"]);

        var names = await scope.ImportAsync(fixture.Project.Id, new(Guid.NewGuid(), env, "Names", ImportIdentifier.DisplayName, "user", "Twin\nCarol"));
        var ambiguous = (await scope.EntriesAsync(fixture.Project.Id, names, "Unresolved", new())).Items.Single();
        ambiguous.Resolution.ShouldBe(ImportResolution.Ambiguous);
        ambiguous.CandidateCount.ShouldBe(2);
        (await scope.GetPopulationAsync(fixture.Project.Id, names)).MemberCount.ShouldBe(1);
        var candidates = await scope.CandidatesAsync(fixture.Project.Id, ambiguous.Id);
        candidates.Select(x => x.ObjectId).ShouldBe([ids["twin1"], ids["twin2"]], ignoreOrder: true);
        await Should.ThrowAsync<ConflictException>(() => scope.ChooseAsync(fixture.Project.Id, ambiguous.Id, ids["alice"]));
        await scope.ChooseAsync(fixture.Project.Id, ambiguous.Id, ids["twin2"]);
        (await scope.EntriesAsync(fixture.Project.Id, names, nameof(ImportResolution.Chosen), new())).Items.Single().ObjectId.ShouldBe(ids["twin2"]);
        (await scope.GetPopulationAsync(fixture.Project.Id, names)).MemberCount.ShouldBe(2);
        (await scope.RefreshPopulationAsync(fixture.Project.Id, names)).ShouldBe(2);
        await Should.ThrowAsync<ArgumentException>(() => scope.ImportAsync(fixture.Project.Id, new(Guid.NewGuid(), env, "Too many",
            ImportIdentifier.Smtp, null, string.Join('\n', Enumerable.Range(0, ScopeRules.MaxImportLines + 1).Select(i => $"u{i}@example.test")))));

        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.AuditEvents.CountAsync(x => x.Action == "Scope.ListImported")).ShouldBe(3);
        (await db.AuditEvents.CountAsync(x => x.Action == "Scope.EntryResolved")).ShouldBe(1);
        (await db.AuditEvents.CountAsync(x => x.Action == "Scope.PopulationCreated")).ShouldBe(6);
    }

    [SqlFact]
    public async Task DecisionsPreviewApplyReplayHistorizeAndFlagOnlyChangedObjects()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var ids = await PublishAsync(fixture);
        var scope = Service(fixture);
        var project = fixture.Project.Id;
        var ou = await scope.CreatePopulationAsync(project, new(Guid.NewGuid(), fixture.Environment.Id, "Sales OU", PopulationKind.OrganizationalUnit, Sales));
        var batch = new ScopeBatch(Guid.NewGuid(), ScopeValue.Include, "  Vague 1 ", "Synthetic wave planning", ou);
        (await scope.PreviewAsync(project, batch)).ShouldBe(new ScopePreview(2, 2, 0));
        await Should.ThrowAsync<ConflictException>(() => scope.ApplyAsync(project, batch, 1));
        await scope.ApplyAsync(project, batch, 2);
        await scope.ApplyAsync(project, batch, 2);
        (await scope.PreviewAsync(project, batch with { OperationId = Guid.NewGuid() })).ShouldBe(new ScopePreview(2, 0, 2));
        await Should.ThrowAsync<ArgumentException>(() => scope.PreviewAsync(project, batch with { ObjectIds = [ids["bob"]] }));
        await scope.ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.ToArbitrate, null, "Disabled account to arbitrate", ObjectIds: [ids["bob"]]), 1);

        var summary = await scope.SummaryAsync(project, EnvironmentRole.Source);
        summary.Populations.ShouldBe(1);
        summary.Counts.ShouldBe([new ScopeCount(ScopeValue.Include, "Vague 1", 1), new ScopeCount(ScopeValue.ToArbitrate, null, 1)]);
        var history = (await scope.HistoryAsync(project, ids["bob"], new())).Items;
        history.Select(x => (x.Number, x.Value, x.Wave)).ShouldBe([(2L, ScopeValue.ToArbitrate, (string?)null), (1L, ScopeValue.Include, "Vague 1")]);
        history.ShouldAllBe(x => x.EvidenceObservationId != null && x.ActorObjectId == fixture.Identity.ObjectId);
        history[1].Population.ShouldBe("Sales OU");
        (await scope.DecisionsAsync(project, EnvironmentRole.Source, "Include", new())).Items.Single().ObjectId.ShouldBe(ids["alice"]);
        (await scope.DecisionsAsync(project, EnvironmentRole.Source, "All", new())).Items.ShouldAllBe(x => !x.NeedsReview);

        await PublishAsync(fixture, aliceUpn: "alice.renamed@example.test", day: 1);
        var rows = (await scope.DecisionsAsync(project, EnvironmentRole.Source, "All", new())).Items.ToDictionary(x => x.ObjectId);
        rows[ids["alice"]].NeedsReview.ShouldBeTrue();
        rows[ids["alice"]].Value.ShouldBe(ScopeValue.Include);
        rows[ids["bob"]].NeedsReview.ShouldBeFalse();

        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.ScopeDecisionRevisions.CountAsync()).ShouldBe(3);
        (await db.AuditEvents.CountAsync(x => x.Action == "Scope.DecisionsApplied")).ShouldBe(2);
    }

    [SqlFact]
    [Trait("Recette", "R09")]
    public async Task ScopeIsWorkspaceIsolatedAndReadersCannotDecide()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var ids = await PublishAsync(fixture);
        var scope = Service(fixture);
        var population = await scope.CreatePopulationAsync(fixture.Project.Id, new(Guid.NewGuid(), fixture.Environment.Id, "Sales OU",
            PopulationKind.OrganizationalUnit, Sales));
        await scope.ApplyAsync(fixture.Project.Id, new(Guid.NewGuid(), ScopeValue.Exclude, null, "Synthetic exclusion", population), 2);

        var other = await fixture.OtherWorkspaceAsync();
        var foreign = new ScopeService(other.Factory, other.Access, other.Scope);
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.PopulationsAsync(fixture.Project.Id, EnvironmentRole.Source, new()));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.GetPopulationAsync(other.Project.Id, population));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.MembersAsync(other.Project.Id, population, new()));
        (await foreign.HistoryAsync(other.Project.Id, ids["alice"], new())).Items.ShouldBeEmpty();
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.PreviewAsync(other.Project.Id,
            new(Guid.NewGuid(), ScopeValue.Include, null, "Foreign", ObjectIds: [ids["alice"]])));

        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        }
        (await scope.SummaryAsync(fixture.Project.Id, EnvironmentRole.Source)).Counts.Single().Count.ShouldBe(2);
        await Should.ThrowAsync<AccessDeniedException>(() => scope.CreatePopulationAsync(fixture.Project.Id,
            new(Guid.NewGuid(), fixture.Environment.Id, "Denied", PopulationKind.Domain, "example.test")));
        await Should.ThrowAsync<AccessDeniedException>(() => scope.ApplyAsync(fixture.Project.Id,
            new(Guid.NewGuid(), ScopeValue.Include, null, "Denied", population), 2));
        await Should.ThrowAsync<AccessDeniedException>(() => scope.RefreshPopulationAsync(fixture.Project.Id, population));

        await using var outside = PersistenceTests.Create(fixture.ConnectionString, []);
        (await outside.Populations.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.PopulationMembers.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.ScopeDecisions.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        (await outside.ScopeDecisionRevisions.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    internal static ScopeService Service(DiscoveryFixture fixture) => new(fixture.Factory, fixture.Access, fixture.Scope);

    /// <summary>Publishes a synthetic AD domain: Sales users (alice enabled, bob disabled), Finance users (carol and two "Twin" homonyms) and nested groups.</summary>
    internal static async Task<Dictionary<string, Guid>> PublishAsync(DiscoveryFixture fixture, string aliceUpn = "alice@example.test", int day = 0)
    {
        var date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(day);
        var scope = new CollectionScope("domain", "example.test");
        const string users = "ad.users:example.test", groups = "ad.groups:example.test";
        static Dictionary<string, JsonElement> Attributes(params (string Name, object Value)[] values) =>
            values.ToDictionary(x => x.Name, x => JsonSerializer.SerializeToElement(x.Value));
        ObjectRecord User(string id, string name, string upn, string ou, bool enabled) => new(users, "user", "ad.objectGUID", id, date, Attributes(
            ("displayName", name), ("userPrincipalName", upn), ("mail", upn), ("sAMAccountName", id), ("distinguishedName", $"CN={id},{ou}"),
            ("enabled", enabled)));
        ObjectRecord Group(string id) => new(groups, "group", "ad.objectGUID", id, date, Attributes(("displayName", id),
            ("distinguishedName", $"CN={id},OU=Groups,DC=example,DC=test")));
        var finance = "OU=Finance,DC=example,DC=test";
        ObjectRecord[] userObjects = [User("alice", "Alice", aliceUpn, Sales, true), User("bob", "Bob", "bob@example.test", Sales, false),
            User("carol", "Carol", "carol@example.test", finance, true), User("twin1", "Twin", "twin1@example.test", finance, true),
            User("twin2", "Twin", "twin2@example.test", finance, true)];
        ObjectRecord[] groupObjects = [Group("sales"), Group("nested")];
        ExternalReference Ref(string type, string id) => new(type, "ad.objectGUID", id);
        RelationshipRecord[] links = [new(groups, "memberOf", Ref("user", "alice"), Ref("group", "sales"), date),
            new(groups, "memberOf", Ref("group", "nested"), Ref("group", "sales"), date),
            new(groups, "memberOf", Ref("user", "bob"), Ref("group", "nested"), date)];
        var package = new DiscoveryPackage(
            new PackageManifest("1.0", Guid.NewGuid(), "clarity.ad", "1.0.0", "AdForest", "forest.example.test", scope, date, date.AddMinutes(1),
                new Dictionary<string, string>(), "Synthetic"),
            [.. userObjects, .. groupObjects], links,
            [new SectionResult(users, "ad.users", CoverageStatus.Complete, scope,
                    ["displayName", "userPrincipalName", "mail", "sAMAccountName", "distinguishedName", "enabled"], [], userObjects.Length, 0, [], []),
                new SectionResult(groups, "ad.groups", CoverageStatus.Complete, scope, ["displayName", "distinguishedName"], ["memberOf"],
                    groupObjects.Length, links.Length, [], [])]);
        DiscoveryCatalog.Load().ValidatePackage(package);
        var run = await fixture.StageAsync(package);
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, run.Id, package, users);
        await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, run.Id, package, groups);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        return await db.ExternalKeys.Where(k => k.Namespace == "ad.objectGUID").ToDictionaryAsync(k => k.ExternalId, k => k.ObjectId);
    }
}
