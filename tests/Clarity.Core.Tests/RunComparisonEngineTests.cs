using System.Text.Json;
using Clarity.Contracts;
using Shouldly;

namespace Clarity.Core.Tests;

public class RunComparisonEngineTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Earlier.AddDays(7);

    private static ComparedSection Section(string capability, bool exhaustive = true, string stream = "teams|1.0", string[]? fields = null,
        string scope = "tenant") =>
        new(Guid.NewGuid(), capability, scope, new CollectionScope(scope, "contoso"), stream + "|" + capability, exhaustive,
            exhaustive, exhaustive ? CoverageStatus.Complete : CoverageStatus.Partial, (fields ?? ["displayName", "visibility"]).ToHashSet());

    private static ComparedObject Row(ComparedSection section, Guid id, string type, DateTimeOffset at, object attributes, bool tombstone = false) =>
        new(section.Id, id, type, at, JsonSerializer.SerializeToElement(attributes).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
            tombstone);

    [Fact]
    public void Exhaustive_comparable_sections_prove_additions_modifications_and_removals()
    {
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var before = Section("teams.teams");
        var after = Section("teams.teams");
        var result = RunComparisonEngine.Compare(
            new([before], [Row(before, a, "team", Earlier, new { displayName = "A", visibility = "private" }),
                Row(before, b, "team", Earlier, new { displayName = "B", visibility = "public" }),
                Row(before, d, "team", Earlier, new { displayName = "D", visibility = "public" })],
                [new(before.Id, "h1"), new(before.Id, "h2")]),
            new([after], [Row(after, a, "team", Later, new { displayName = "A", visibility = "public" }),
                Row(after, c, "team", Later, new { displayName = "C", visibility = "public" }),
                Row(after, d, "team", Later, new { displayName = "D", visibility = "public" })],
                [new(after.Id, "h2"), new(after.Id, "h3")]));

        result.Items.Single(i => i.ObjectId == a).ShouldSatisfyAllConditions(
            i => i.Change.ShouldBe(RunChange.Modified),
            i => i.ChangedFields.ShouldBe(["visibility"]),
            i => i.Changes.Single().ShouldBe(new FieldChange("visibility", "private", "public")),
            i => i.AssessmentReview.ShouldBeTrue(),
            i => i.ScopeReview.ShouldBeFalse());
        result.Items.Single(i => i.ObjectId == b).ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.Removed), i => i.ScopeReview.ShouldBeTrue(),
            i => i.AssessmentReview.ShouldBeTrue(), i => i.Tombstone.ShouldBeFalse());
        result.Items.Single(i => i.ObjectId == c).ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.Added), i => i.ScopeReview.ShouldBeFalse());
        result.Items.ShouldNotContain(i => i.ObjectId == d);
        result.Count(RunChange.Unchanged).ShouldBe(1);
        result.Units.Single().ShouldSatisfyAllConditions(u => u.Comparison.ShouldBe(UnitComparison.Comparable), u => u.RelationsAdded.ShouldBe(1),
            u => u.RelationsRemoved.ShouldBe(1), u => u.RelationsUnknown.ShouldBe(0), u => u.BaselineObjects.ShouldBe(3), u => u.LaterObjects.ShouldBe(3));
    }

    [Fact]
    public void Partial_or_unknown_coverage_never_fabricates_a_deletion()
    {
        var (kept, missing, fresh) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var before = Section("teams.sites", fields: ["webUrl", "teamsConnected"]);
        var after = Section("teams.sites", exhaustive: false, fields: ["webUrl", "teamsConnected"]);
        var result = RunComparisonEngine.Compare(
            new([before], [Row(before, kept, "site", Earlier, new { webUrl = "https://x/sites/a" }),
                Row(before, missing, "site", Earlier, new { webUrl = "https://x/sites/b" })], [new(before.Id, "h1")]),
            new([after], [Row(after, kept, "site", Later, new { webUrl = "https://x/sites/a" }),
                Row(after, fresh, "site", Later, new { webUrl = "https://x/sites/c" })], []));

        result.Items.Single(i => i.ObjectId == missing).ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.NotObserved),
            i => i.ScopeReview.ShouldBeFalse(), i => i.MappingReview.ShouldBeFalse(), i => i.AssessmentReview.ShouldBeFalse());
        result.Items.Single(i => i.ObjectId == fresh).Change.ShouldBe(RunChange.NewlyObserved);
        result.Count(RunChange.Removed).ShouldBe(0);
        result.Units.Single().ShouldSatisfyAllConditions(u => u.Comparison.ShouldBe(UnitComparison.NotExhaustive),
            u => u.LaterCoverage.ShouldBe(CoverageStatus.Partial), u => u.RelationsRemoved.ShouldBe(0), u => u.RelationsUnknown.ShouldBe(1));
    }

    [Fact]
    public void Different_producer_versions_and_one_sided_sections_are_not_comparable()
    {
        var gone = Guid.NewGuid();
        var before = Section("teams.teams", stream: "teams|1.0");
        var after = Section("teams.teams", stream: "teams|2.0");
        var onlyBefore = Section("teams.channels", fields: ["displayName"]);
        var result = RunComparisonEngine.Compare(
            new([before, onlyBefore], [Row(before, gone, "team", Earlier, new { displayName = "Gone" }),
                Row(onlyBefore, Guid.NewGuid(), "channel", Earlier, new { displayName = "General" })], [new(onlyBefore.Id, "c1")]),
            new([after], [], []));

        result.Items.Single(i => i.ObjectId == gone).Change.ShouldBe(RunChange.NotObserved);
        result.Units.Single(u => u.CapabilityId == "teams.teams").Comparison.ShouldBe(UnitComparison.DifferentStream);
        result.Units.Single(u => u.CapabilityId == "teams.channels").ShouldSatisfyAllConditions(u => u.Comparison.ShouldBe(UnitComparison.BaselineOnly),
            u => u.RelationsUnknown.ShouldBe(1));
        result.Count(RunChange.Removed).ShouldBe(0);
    }

    [Fact]
    public void A_source_tombstone_is_a_removal_even_without_exhaustive_coverage()
    {
        var user = Guid.NewGuid();
        var before = Section("entra.users", exhaustive: false, fields: ["userPrincipalName", "deleted"]);
        var after = Section("entra.users", exhaustive: false, fields: ["userPrincipalName", "deleted"]);
        var result = RunComparisonEngine.Compare(
            new([before], [Row(before, user, "user", Earlier, new { userPrincipalName = "a@contoso.example" })], []),
            new([after], [Row(after, user, "user", Later, new { userPrincipalName = "a@contoso.example", deleted = true }, tombstone: true)], []));

        result.Items.Single().ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.Removed), i => i.Tombstone.ShouldBeTrue(),
            i => i.ScopeReview.ShouldBeTrue(), i => i.MappingReview.ShouldBeTrue());
    }

    [Fact]
    public void Critical_fields_flag_only_the_decisions_they_invalidate()
    {
        var user = Guid.NewGuid();
        var before = Section("entra.users", exhaustive: false, fields: ["displayName", "userPrincipalName", "department"]);
        var after = Section("entra.users", exhaustive: false, fields: ["displayName", "userPrincipalName", "department"]);
        var renamed = RunComparisonEngine.Compare(
            new([before], [Row(before, user, "user", Earlier, new { displayName = "Alice", department = "Sales" })], []),
            new([after], [Row(after, user, "user", Later, new { displayName = "Alice Martin", department = "Finance" })], [])).Items.Single();
        renamed.ShouldSatisfyAllConditions(i => i.Change.ShouldBe(RunChange.Modified), i => i.ScopeReview.ShouldBeFalse(),
            i => i.MappingReview.ShouldBeFalse(), i => i.AssessmentReview.ShouldBeFalse());

        var moved = RunComparisonEngine.Compare(
            new([before], [Row(before, user, "user", Earlier, new { userPrincipalName = "alice@contoso.example" })], []),
            new([after], [Row(after, user, "user", Later, new { userPrincipalName = "alice@fabrikam.example" })], [])).Items.Single();
        moved.ShouldSatisfyAllConditions(i => i.ScopeReview.ShouldBeTrue(), i => i.MappingReview.ShouldBeTrue(), i => i.AssessmentReview.ShouldBeFalse());
    }

    [Fact]
    public void A_field_missing_from_a_partial_row_is_unknown_not_removed()
    {
        var user = Guid.NewGuid();
        var full = Section("entra.users", exhaustive: true, fields: ["mail", "userPrincipalName"]);
        var partial = Section("entra.users", exhaustive: false, fields: ["mail", "userPrincipalName"]);
        var result = RunComparisonEngine.Compare(
            new([full], [Row(full, user, "user", Earlier, new { mail = "a@contoso.example", userPrincipalName = "a@contoso.example" })], []),
            new([partial], [Row(partial, user, "user", Later, new { userPrincipalName = "a@contoso.example" })], []));
        result.Count(RunChange.Unchanged).ShouldBe(1);

        var cleared = RunComparisonEngine.Compare(
            new([partial], [Row(partial, user, "user", Earlier, new { mail = "a@contoso.example", userPrincipalName = "a@contoso.example" })], []),
            new([full], [Row(full, user, "user", Later, new { userPrincipalName = "a@contoso.example" })], []));
        cleared.Items.Single().Changes.Single().ShouldBe(new FieldChange("mail", "a@contoso.example", null));
    }

    [Fact]
    public void Decision_review_ignores_additions_and_unproven_absences()
    {
        foreach (var change in new[] { RunChange.Added, RunChange.NewlyObserved, RunChange.NotObserved, RunChange.NotCompared, RunChange.Unchanged })
        {
            foreach (var kind in Enum.GetValues<DecisionKind>())
            {
                DecisionReview.Affects(kind, "user", change, ["userPrincipalName", "visibility", "appId"]).ShouldBeFalse();
            }
        }
        DecisionReview.Affects(DecisionKind.Mapping, "team", RunChange.Removed, []).ShouldBeFalse();
        DecisionReview.Affects(DecisionKind.Mapping, "device", RunChange.Modified, ["displayName"]).ShouldBeTrue();
        DecisionReview.Affects(DecisionKind.SsoDependency, "servicePrincipal", RunChange.Modified, ["preferredSingleSignOnMode"]).ShouldBeTrue();
    }
}
