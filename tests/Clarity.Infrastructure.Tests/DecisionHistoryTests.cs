using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class DecisionHistoryTests
{
    [SqlFact]
    [Trait("Recette", "R12")]
    public async Task TargetedRefreshFlagsOnlyChangedDecisionsAndReconfirmationClearsThem()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var ids = await ScopeTests.PublishAsync(fixture);
        var scope = ScopeTests.Service(fixture);
        var register = Register(fixture);
        var project = fixture.Project.Id;
        const string reason = "Synthetic wave planning";
        await scope.ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.Include, "Vague 1", reason, ObjectIds: [ids["alice"], ids["bob"]]), 2);
        (await register.SummaryAsync(project, EnvironmentRole.Source)).Single(x => x.Kind == DecisionKind.Scope).ShouldBe(new(DecisionKind.Scope, 2, 0));

        await ScopeTests.PublishAsync(fixture, aliceUpn: "alice.renamed@example.test", day: 1);
        var row = (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, true, new())).Items.ShouldHaveSingleItem();
        (row.ObjectId, row.ValueKey, row.Wave, row.Revision, row.Reason, row.AuthorObjectId, row.NeedsReview)
            .ShouldBe((ids["alice"], "ScopeDecision_Include", (string?)"Vague 1", 1L, (string?)reason, (Guid?)fixture.Identity.ObjectId, true));
        row.Changes.ShouldBe([new BasisChange("mail", "\"alice@example.test\"", "\"alice.renamed@example.test\""),
            new BasisChange("userPrincipalName", "\"alice@example.test\"", "\"alice.renamed@example.test\"")]);
        (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, false, new())).Items.Single(x => x.ObjectId == ids["bob"])
            .NeedsReview.ShouldBeFalse();
        (await register.SummaryAsync(project, EnvironmentRole.Source)).Single(x => x.Kind == DecisionKind.Scope).ShouldBe(new(DecisionKind.Scope, 2, 1));
        var since = row.FlaggedAtUtc!.Value;

        await ScopeTests.PublishAsync(fixture, aliceUpn: "alice.again@example.test", day: 2);
        var again = (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, true, new())).Items.ShouldHaveSingleItem();
        again.FlaggedAtUtc.ShouldBe(since);
        again.Changes!.Single(x => x.Field == "userPrincipalName").After.ShouldBe("\"alice.again@example.test\"");
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.ScopeDecisions.Select(x => x.Revision).ToListAsync()).ShouldAllBe(x => x == 1);
            (await db.ScopeDecisionRevisions.CountAsync()).ShouldBe(2);
        }

        var reconfirm = new ScopeBatch(Guid.NewGuid(), ScopeValue.Include, "Vague 1", "Renamed account confirmed", ObjectIds: [ids["alice"]]);
        (await scope.PreviewAsync(project, reconfirm)).ShouldBe(new ScopePreview(1, 1, 0, 1));
        await scope.ApplyAsync(project, reconfirm, 1);
        (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, true, new())).Items.ShouldBeEmpty();
        (await scope.HistoryAsync(project, ids["alice"], new())).Items.Select(x => (x.Number, x.Reason))
            .ShouldBe([(2L, "Renamed account confirmed"), (1L, reason)]);
        (await scope.PreviewAsync(project, reconfirm with { OperationId = Guid.NewGuid() })).ShouldBe(new ScopePreview(1, 0, 1));

        await ScopeTests.PublishAsync(fixture, aliceUpn: "alice.again@example.test", day: 3);
        (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, true, new())).Items.ShouldBeEmpty();
    }

    [SqlFact]
    [Trait("Recette", "R09")]
    [Trait("Recette", "R12")]
    public async Task RecomputeFlagsLegacyDecisionsWithUnknownChangesAndIsAuthorizedAndIsolated()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var ids = await ScopeTests.PublishAsync(fixture);
        var scope = ScopeTests.Service(fixture);
        var register = Register(fixture);
        var project = fixture.Project.Id;
        await scope.ApplyAsync(project, new(Guid.NewGuid(), ScopeValue.Exclude, null, "Synthetic exclusion", ObjectIds: [ids["alice"], ids["bob"]]), 2);
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            await db.ScopeDecisions.Where(x => x.ObjectId == ids["alice"])
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Fingerprint, "LEGACY").SetProperty(x => x.BasisJson, (string?)null));
        }
        (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, true, new())).Items.ShouldBeEmpty();

        (await register.RecomputeAsync(project)).ShouldBe(1);
        var legacy = (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, true, new())).Items.ShouldHaveSingleItem();
        (legacy.ObjectId, legacy.NeedsReview, legacy.Changes).ShouldBe((ids["alice"], true, (IReadOnlyList<BasisChange>?)null));
        (await register.PageAsync(project, EnvironmentRole.Target, DecisionKind.Scope, false, new())).Items.ShouldBeEmpty();
        (await register.PageAsync(project, EnvironmentRole.Source, DecisionKind.Scope, false, new(Search: "ali"))).Items.ShouldHaveSingleItem();

        var other = await fixture.OtherWorkspaceAsync();
        var foreign = new DecisionRegisterService(other.Factory, other.Access, other.Scope);
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.SummaryAsync(project, EnvironmentRole.Source));
        await Should.ThrowAsync<AccessDeniedException>(() => foreign.RecomputeAsync(project));
        (await foreign.PageAsync(other.Project.Id, EnvironmentRole.Source, DecisionKind.Scope, false, new())).Items.ShouldBeEmpty();

        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            (await db.AuditEvents.CountAsync(x => x.Action == "DecisionReview.Recomputed" && x.ResourceId == project)).ShouldBe(1);
            await db.Members.ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, WorkspaceRole.Reader));
        }
        (await register.SummaryAsync(project, EnvironmentRole.Source)).Single(x => x.Kind == DecisionKind.Scope).ToReview.ShouldBe(1);
        await Should.ThrowAsync<AccessDeniedException>(() => register.RecomputeAsync(project));

        await using var outside = PersistenceTests.Create(fixture.ConnectionString, []);
        (await outside.ReviewFlags.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    private static DecisionRegisterService Register(DiscoveryFixture fixture) => new(fixture.Factory, fixture.Access, fixture.Scope);
}
