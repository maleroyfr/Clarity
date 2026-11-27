using Clarity.Contracts;
using Shouldly;

namespace Clarity.Core.Tests;

public class DependencyEngineTests
{
    private readonly List<DependencyNode> _nodes = [];
    private readonly List<DependencyEdge> _edges = [];
    private readonly Dictionary<Guid, DependencySide> _sides = [];
    private readonly HashSet<Guid> _all = [], _excluded = [];
    private readonly Dictionary<Guid, string> _channels = [];

    private Guid Node(string type, DependencySide? side = null, bool deleted = false)
    {
        var id = Guid.NewGuid();
        _nodes.Add(new(id, type, deleted));
        if (side is { } value) { _sides[id] = value; }
        return id;
    }

    private Guid Edge(string relation, Guid? from, Guid? to)
    {
        var id = Guid.NewGuid();
        _edges.Add(new(id, relation, from, to));
        return id;
    }

    private DependencyGraphResult Analyze(long budget = DependencyRules.MaxTraversal) =>
        DependencyEngine.Analyze(new(_nodes, _edges, _sides, _all, _excluded, _channels), budget);

    [Fact]
    public void Shared_team_and_site_count_once_with_both_populations_and_routes()
    {
        var alice = Node("user", DependencySide.Migrating);
        var bob = Node("user", DependencySide.Remaining);
        var team = Node("team");
        var group = Node("group");
        var site = Node("site");
        Edge("teamGroup", team, group);
        Edge("memberOf", alice, team);
        Edge("memberOf", bob, team);
        Edge("memberOf", alice, group);
        var viaGroup = Edge("groupSite", group, site);
        var viaTeam = Edge("teamSite", team, site);

        var result = Analyze();

        result.Findings.Count(f => f.ObjectId == site).ShouldBe(1);
        var shared = result.Findings.Single(f => f.ObjectId == site);
        shared.Classification.ShouldBe(DependencyClassification.Shared);
        (shared.Migrating, shared.Remaining).ShouldBe((1, 1));
        shared.Routes.Select(r => (r.ObjectId, r.Relation, r.RelationshipId)).OrderBy(r => r.Relation)
            .ShouldBe([(group, "groupSite", viaGroup), (team, "teamSite", viaTeam)]);
        shared.Samples.Single(s => s.UserId == bob).Side.ShouldBe(DependencySide.Remaining);
        result.Findings.Single(f => f.ObjectId == team).Classification.ShouldBe(DependencyClassification.Shared);
        result.Findings.Single(f => f.ObjectId == group).Classification.ShouldBe(DependencyClassification.Shared);
        (result.Migrating, result.Remaining).ShouldBe((1, 1));
    }

    [Fact]
    public void Nested_membership_explains_the_route_and_unrelated_resources_stay_out()
    {
        var alice = Node("user", DependencySide.Migrating);
        var carol = Node("user", DependencySide.Remaining);
        var inner = Node("group");
        var outer = Node("group");
        var site = Node("site");
        var other = Node("site");
        Edge("memberOf", alice, inner);
        Edge("memberOf", inner, outer);
        Edge("groupSite", outer, site);
        Edge("memberOf", carol, Node("group"));
        Edge("sitePermission", carol, other);

        var result = Analyze();

        var found = result.Findings.Single(f => f.ObjectId == site);
        found.Classification.ShouldBe(DependencyClassification.MigratingOnly);
        found.Samples.Single().Route.ShouldBe("memberOf › memberOf › groupSite");
        found.Samples.Single().Via.ShouldBe(outer);
        result.Findings.ShouldNotContain(f => f.ObjectId == other);
    }

    [Fact]
    public void Delegation_is_not_transitive_through_another_resource()
    {
        var alice = Node("user", DependencySide.Migrating);
        var bob = Node("user", DependencySide.Remaining);
        var dave = Node("user", DependencySide.Remaining);
        var aliceMailbox = Node("mailbox");
        var shared = Node("sharedMailbox");
        Edge("mailboxOf", aliceMailbox, alice);
        Edge("fullAccessTo", alice, shared);
        Edge("fullAccessTo", bob, aliceMailbox);
        Edge("sendAsOn", dave, aliceMailbox);

        var result = Analyze();

        var own = result.Findings.Single(f => f.ObjectId == aliceMailbox);
        (own.Migrating, own.Remaining).ShouldBe((1, 2));
        own.Classification.ShouldBe(DependencyClassification.Shared);
        var delegated = result.Findings.Single(f => f.ObjectId == shared);
        (delegated.Migrating, delegated.Remaining).ShouldBe((1, 0));
    }

    [Fact]
    public void Private_channel_roster_is_a_coverage_gap_not_team_members()
    {
        var alice = Node("user", DependencySide.Migrating);
        var bob = Node("user", DependencySide.Remaining);
        var team = Node("team");
        var standard = Node("teamChannel");
        var secret = Node("teamChannel");
        var teamSite = Node("site");
        var secretSite = Node("site");
        _channels[standard] = "standard";
        _channels[secret] = "private";
        Edge("memberOf", alice, team);
        Edge("memberOf", bob, team);
        Edge("channelInTeam", standard, team);
        Edge("channelInTeam", secret, team);
        Edge("channelSite", standard, teamSite);
        Edge("channelSite", secret, secretSite);

        var result = Analyze();

        result.Findings.Single(f => f.ObjectId == teamSite).Classification.ShouldBe(DependencyClassification.Shared);
        result.Findings.ShouldNotContain(f => f.ObjectId == secretSite);
        result.Gaps.Select(g => (g.ObjectId, g.Family)).ShouldBe([(secret, DependencyFamily.Teams), (secretSite, DependencyFamily.Sites)], ignoreOrder: true);
    }

    [Fact]
    public void Unresolved_members_are_counted_without_inventing_a_side()
    {
        var alice = Node("user", DependencySide.Migrating);
        var team = Node("team");
        var opaque = Node("teamMembership");
        Edge("memberOf", alice, team);
        Edge("memberOf", opaque, team);
        Edge("memberOf", null, team);

        var found = Analyze().Findings.Single(f => f.ObjectId == team);

        found.Unresolved.ShouldBe(2);
        (found.Migrating, found.Remaining).ShouldBe((1, 0));
        found.Classification.ShouldBe(DependencyClassification.MigratingOnly);
    }

    [Fact]
    public void All_users_assignment_counts_everyone_and_exclusion_targets_are_ignored()
    {
        var alice = Node("user", DependencySide.Migrating);
        Node("user", DependencySide.Remaining);
        Node("user");
        Node("user", DependencySide.Remaining, deleted: true);
        var app = Node("mobileApp");
        var everyone = Node("mobileAppAssignment");
        Edge("assignmentOf", everyone, app);
        _all.Add(everyone);
        var policy = Node("deviceCompliancePolicy");
        var group = Node("group");
        var exclusion = Node("deviceCompliancePolicyAssignment");
        Edge("memberOf", alice, group);
        Edge("assignmentOf", exclusion, policy);
        Edge("targetsGroup", exclusion, group);
        _excluded.Add(exclusion);

        var result = Analyze();

        var found = result.Findings.Single(f => f.ObjectId == app);
        found.AllUsers.ShouldBeTrue();
        (found.Migrating, found.Remaining).ShouldBe((1, 2));
        result.Findings.ShouldNotContain(f => f.ObjectId == policy);
    }

    [Fact]
    public void Waves_and_arbitration_change_the_classification()
    {
        var alice = Node("user", DependencySide.Migrating);
        var erin = Node("user", DependencySide.OtherWave);
        var frank = Node("user", DependencySide.ToArbitrate);
        var waves = Node("group");
        var arbitrated = Node("group");
        Edge("memberOf", alice, waves);
        Edge("memberOf", erin, waves);
        Edge("memberOf", alice, arbitrated);
        Edge("memberOf", frank, arbitrated);

        var result = Analyze();

        result.Findings.Single(f => f.ObjectId == waves).Classification.ShouldBe(DependencyClassification.CrossWave);
        result.Findings.Single(f => f.ObjectId == arbitrated).Classification.ShouldBe(DependencyClassification.ToArbitrate);
    }

    [Fact]
    public void Device_owner_and_managed_device_pairing_are_followed()
    {
        var alice = Node("user", DependencySide.Migrating);
        var bob = Node("user", DependencySide.Remaining);
        var managed = Node("managedDevice");
        var device = Node("device");
        Edge("intunePrimaryUserOf", alice, managed);
        Edge("referencesEntraDevice", managed, device);
        Edge("entraRegisteredOwnerOf", bob, device);

        var result = Analyze();

        result.Findings.Single(f => f.ObjectId == managed).Classification.ShouldBe(DependencyClassification.Shared);
        result.Findings.Single(f => f.ObjectId == device).Classification.ShouldBe(DependencyClassification.Shared);
    }

    [Fact]
    public void Traversal_budget_fails_explicitly()
    {
        var alice = Node("user", DependencySide.Migrating);
        var group = Node("group");
        Edge("memberOf", alice, group);
        Should.Throw<ArgumentException>(() => Analyze(budget: 1));
    }

    [Fact]
    public void Coverage_is_complete_only_for_complete_tenant_sections_without_gaps()
    {
        var complete = new DependencySectionCoverage("teams.teams", CoverageStatus.Complete, DateTimeOffset.UnixEpoch, false);
        DependencyRules.Status([complete], 0, 0).ShouldBe(DependencyCoverage.Complete);
        DependencyRules.Status([complete], 1, 0).ShouldBe(DependencyCoverage.Partial);
        DependencyRules.Status([complete], 0, 1).ShouldBe(DependencyCoverage.Partial);
        DependencyRules.Status([complete with { ResourceScoped = true }], 0, 0).ShouldBe(DependencyCoverage.Partial);
        DependencyRules.Status([complete, complete with { CapabilityId = "teams.members", Coverage = null }], 0, 0).ShouldBe(DependencyCoverage.Partial);
        DependencyRules.Status([complete with { Coverage = CoverageStatus.Failed }], 0, 0).ShouldBe(DependencyCoverage.NotCollected);
        DependencyRules.Status([], 0, 0).ShouldBe(DependencyCoverage.NotCollected);
    }

    [Fact]
    public void Coverage_sections_exist_in_the_catalog()
    {
        var catalog = DiscoveryCatalog.Load();
        foreach (var id in DependencyRules.Sections.Values.SelectMany(x => x)) { catalog.Sections.ContainsKey(id).ShouldBeTrue(id); }
        DependencyEngine.Relations.ShouldAllBe(r => catalog.Sections.Values.Any(s => s.Relations.Contains(r)));
    }
}
