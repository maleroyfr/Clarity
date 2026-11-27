using Shouldly;

namespace Clarity.Core.Tests;

public class TargetComparisonEngineTests
{
    private static readonly string[] Fields = ["employeeId", "userPrincipalName", "mail", "proxyAddresses", "alias"];

    private static TargetCandidate Obj(string type, string label, Guid? owner, params (string Field, string Value)[] values) =>
        new(Guid.NewGuid(), type, label, values.GroupBy(v => v.Field).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(v => v.Value).ToList()),
            new HashSet<string>(), owner);

    private static TargetCandidate User(string label, params (string Field, string Value)[] values) => Obj("user", label, null, values);

    private static TargetSideData Side(IReadOnlyList<TargetCandidate> objects, bool exhaustive = true, string[]? fields = null,
        IReadOnlyList<ObservedDomain>? domains = null) =>
        new(objects, objects.Select(o => o.Type).Append("user").Distinct().ToDictionary(t => t,
            _ => new TargetCoverage((fields ?? Fields).ToHashSet(), exhaustive)), domains ?? [], domains is not null);

    private static TargetRuleInput Shared(int priority = 1) =>
        new(Guid.NewGuid(), "user", TargetRuleKind.SharedAttribute, "employeeId", "employeeId", null, priority);

    private static TargetRuleInput Computed(string domain = "fabrikam.example") =>
        new(Guid.NewGuid(), "user", TargetRuleKind.ComputedUpn, "userPrincipalName", "userPrincipalName", domain, 1);

    [Fact]
    public void A_shared_attribute_matches_one_target_and_several_candidates_stay_ambiguous()
    {
        var (a, b) = (User("Ann", ("employeeId", "E1")), User("Bob", ("employeeId", "E2")));
        var (t1, t2, t3) = (User("Ann T", ("employeeId", "e1")), User("Bob T1", ("employeeId", "E2")), User("Bob T2", ("employeeId", "E2")));
        var rule = Shared();

        var result = TargetComparisonEngine.Compare([rule], Side([a, b]), Side([t1, t2, t3]), TargetDataState.Available);

        result.Outcomes.Single(o => o.SourceId == a.Id).ShouldSatisfyAllConditions(
            o => o.Outcome.ShouldBe(TargetOutcome.Matched), o => o.Reason.ShouldBe("SingleCandidate"),
            o => o.Matches.Single().ShouldBe(new TargetMatch(t1.Id, rule.Id, "E1")));
        result.Outcomes.Single(o => o.SourceId == b.Id).ShouldSatisfyAllConditions(
            o => o.Outcome.ShouldBe(TargetOutcome.Ambiguous), o => o.Reason.ShouldBe("MultipleCandidates"),
            o => o.Matches.Select(m => m.TargetId).ShouldBe([t2.Id, t3.Id], ignoreOrder: true));
    }

    [Fact]
    public void Two_sources_reaching_one_target_are_ambiguous_instead_of_both_matched()
    {
        var (a, b) = (User("Ann", ("employeeId", "E1")), User("Ann bis", ("employeeId", "E1")));
        var target = User("Ann T", ("employeeId", "E1"));

        var result = TargetComparisonEngine.Compare([Shared()], Side([a, b]), Side([target]), TargetDataState.Available);

        result.Outcomes.ShouldAllBe(o => o.Outcome == TargetOutcome.Ambiguous && o.Reason == "SharedCandidate");
        result.Outcomes.Single(o => o.SourceId == a.Id).Sharers.ShouldBe([b.Id]);
    }

    [Fact]
    public void Absence_requires_an_exhaustive_target_with_the_rule_field_collected_without_conflict()
    {
        var source = User("Ann", ("employeeId", "E9"));
        var other = User("Other", ("employeeId", "E1"));
        TargetOutcomeItem Run(TargetSideData target) =>
            TargetComparisonEngine.Compare([Shared()], Side([source]), target, TargetDataState.Available).Outcomes.Single();

        Run(Side([other])).ShouldSatisfyAllConditions(o => o.Outcome.ShouldBe(TargetOutcome.Absent), o => o.Reason.ShouldBe("NoCandidate"));
        Run(Side([other], exhaustive: false)).Reason.ShouldBe("TargetNotExhaustive");
        Run(Side([other], fields: ["userPrincipalName"])).Reason.ShouldBe("TargetAttributeNotCollected");
        var conflicting = other with { Conflicting = new HashSet<string> { "employeeId" } };
        Run(Side([conflicting])).ShouldSatisfyAllConditions(o => o.Outcome.ShouldBe(TargetOutcome.Unknown), o => o.Reason.ShouldBe("TargetValueConflicting"));
    }

    [Fact]
    public void Missing_or_uncollected_source_values_are_unknown()
    {
        var source = User("Ann", ("userPrincipalName", "ann@contoso.example"));
        var target = Side([User("Other", ("employeeId", "E1"))]);

        TargetComparisonEngine.Compare([Shared()], Side([source]), target, TargetDataState.Available).Outcomes.Single()
            .ShouldSatisfyAllConditions(o => o.Outcome.ShouldBe(TargetOutcome.Unknown), o => o.Reason.ShouldBe("SourceValueMissing"));
        TargetComparisonEngine.Compare([Shared()], Side([source], fields: ["userPrincipalName"]), target, TargetDataState.Available).Outcomes.Single()
            .Reason.ShouldBe("SourceAttributeNotCollected");
    }

    [Fact]
    public void A_planned_target_is_never_compared_but_shared_computed_upns_are_reported()
    {
        var a = User("Ann", ("userPrincipalName", "jdoe@contoso.example"));
        var b = User("John", ("userPrincipalName", "JDoe@legacy.example"));
        var c = User("Carl", ("userPrincipalName", "carl@contoso.example"));
        var rule = Computed();

        var planned = TargetComparisonEngine.Compare([rule], Side([a, b, c]), Side([]), TargetDataState.Planned);
        var missing = TargetComparisonEngine.Compare([rule], Side([a, b, c]), Side([]), TargetDataState.NoData);

        planned.Outcomes.ShouldAllBe(o => o.Outcome == TargetOutcome.NotCompared && o.Reason == "TargetPlanned" && o.Matches.Count == 0);
        planned.Outcomes.Single(o => o.SourceId == c.Id).Key.ShouldBe("carl@fabrikam.example");
        missing.Outcomes.ShouldAllBe(o => o.Reason == "TargetDataMissing");
        planned.Collisions.ShouldAllBe(x => x.Kind == CollisionKind.Upn && x.Reason == "ComputedUpnShared" && x.Key == "jdoe@fabrikam.example");
        planned.Collisions.Select(x => (x.SourceId, x.OtherSourceId)).ShouldBe([(a.Id, b.Id), (b.Id, a.Id)], ignoreOrder: true);
    }

    [Fact]
    public void Taken_upn_smtp_and_alias_ignore_the_matched_counterpart_and_its_mailbox()
    {
        var ann = User("Ann", ("userPrincipalName", "ann@contoso.example"), ("mail", "ann@contoso.example"),
            ("proxyAddresses", "SMTP:ann@contoso.example"), ("proxyAddresses", "smtp:sales@shared.example"),
            ("proxyAddresses", "x500:/o=legacy/cn=ann"), ("alias", "ann"));
        var annTarget = User("Ann T", ("userPrincipalName", "ann@fabrikam.example"), ("mail", "ann@fabrikam.example"), ("alias", "ann"));
        var annMailbox = Obj("mailbox", "Ann T mailbox", annTarget.Id, ("userPrincipalName", "ann@fabrikam.example"), ("alias", "ann"),
            ("proxyAddresses", "SMTP:ann@fabrikam.example"));
        var sales = Obj("sharedMailbox", "Sales", null, ("mail", "sales@shared.example"), ("alias", "ann"));
        var legacy = Obj("mailbox", "Legacy", null, ("proxyAddresses", "X500:/o=legacy/cn=ann"));
        var rule = Computed();

        var result = TargetComparisonEngine.Compare([rule], Side([ann]), Side([annTarget, annMailbox, sales, legacy]), TargetDataState.Available);

        result.Outcomes.Single().ShouldSatisfyAllConditions(o => o.Outcome.ShouldBe(TargetOutcome.Matched), o => o.Matches.Single().TargetId.ShouldBe(annTarget.Id));
        result.Collisions.Select(x => (x.Kind, x.Reason, x.Key, x.TargetId)).ShouldBe([
            (CollisionKind.Smtp, "SmtpTaken", "sales@shared.example", sales.Id),
            (CollisionKind.Alias, "AliasTaken", "ann", sales.Id)], ignoreOrder: true);
    }

    [Fact]
    public void A_computed_upn_held_by_another_target_type_is_reported_once_per_principal()
    {
        var ann = User("Ann", ("userPrincipalName", "ann@contoso.example"));
        var group = Obj("group", "Ann group", null, ("userPrincipalName", "ann@fabrikam.example"));
        var groupMailbox = Obj("mailGroup", "Ann group mail", group.Id, ("userPrincipalName", "ann@fabrikam.example"));
        var other = User("Other", ("userPrincipalName", "other@fabrikam.example"));

        var result = TargetComparisonEngine.Compare([Computed()], Side([ann]), Side([other, group, groupMailbox]), TargetDataState.Available);

        result.Outcomes.Single().ShouldSatisfyAllConditions(o => o.Outcome.ShouldBe(TargetOutcome.Absent), o => o.Key.ShouldBe("ann@fabrikam.example"));
        result.Collisions.Single().ShouldSatisfyAllConditions(x => x.Reason.ShouldBe("TargetUpnTaken"), x => x.TargetId.ShouldBe(group.Id));
    }

    [Fact]
    public void Domains_shared_by_both_sides_and_unverified_rule_domains_are_collisions()
    {
        var sourceDomain = new ObservedDomain(Guid.NewGuid(), "Contoso.example", true);
        var targetDomain = new ObservedDomain(Guid.NewGuid(), "contoso.example", true);
        var unverified = new ObservedDomain(Guid.NewGuid(), "fabrikam.example", false);
        var rule = Computed();

        var result = TargetComparisonEngine.Compare([rule], Side([], domains: [sourceDomain]), Side([], domains: [targetDomain, unverified]),
            TargetDataState.Available);
        var planned = TargetComparisonEngine.Compare([rule], Side([], domains: [sourceDomain]), Side([]), TargetDataState.Planned);

        result.Collisions.Select(x => (x.Reason, x.Key, x.SourceId, x.TargetId, x.RuleId)).ShouldBe([
            ("SharedDomain", "contoso.example", (Guid?)sourceDomain.ObjectId, (Guid?)targetDomain.ObjectId, (Guid?)null),
            ("TargetDomainNotVerified", "fabrikam.example", null, null, rule.Id)], ignoreOrder: true);
        planned.Collisions.ShouldBeEmpty();
    }

    [Fact]
    public void Rules_and_items_are_validated()
    {
        TargetMappingRule Rule(TargetRuleKind kind, string target, string? domain) => new()
        {
            WorkspaceId = Guid.NewGuid(),
            ObjectType = "user",
            Kind = kind,
            SourceField = "employeeId",
            TargetField = target,
            TargetDomain = domain,
            Priority = 1,
            Justification = "HR identifier"
        };
        Should.NotThrow(() => TargetComparisonRules.Validate(Rule(TargetRuleKind.SharedAttribute, "employeeId", null)));
        Should.NotThrow(() => TargetComparisonRules.Validate(Rule(TargetRuleKind.ComputedUpn, "userPrincipalName", "fabrikam.example")));
        Should.Throw<ArgumentException>(() => TargetComparisonRules.Validate(Rule(TargetRuleKind.ComputedUpn, "userPrincipalName", "Fabrikam")));
        Should.Throw<ArgumentException>(() => TargetComparisonRules.Validate(Rule(TargetRuleKind.SharedAttribute, "employee id", null)));
        Should.Throw<ArgumentException>(() => TargetComparisonRules.Validate(new TargetComparisonItem
        {
            WorkspaceId = Guid.NewGuid(),
            ObjectType = "user",
            Outcome = TargetOutcome.Matched,
            SourceObjectId = Guid.NewGuid(),
            Reason = "Invented"
        }));
        TargetComparisonEngine.ComputedUpn(" j doe@contoso.example", "fabrikam.example").ShouldBeNull();
        TargetComparisonEngine.ComputedUpn("J.Doe", "fabrikam.example").ShouldBe("j.doe@fabrikam.example");
    }
}
