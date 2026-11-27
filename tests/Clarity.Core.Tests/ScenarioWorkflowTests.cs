using Shouldly;

namespace Clarity.Core.Tests;

public sealed class ScenarioWorkflowTests
{
    private static readonly ScenarioFacts Collected = new() { Sources = 1, SourceSections = 4 };

    private static ScenarioStepView Step(IEnumerable<ScenarioStepView> steps, ScenarioStep step) => steps.Single(s => s.Step == step);

    [Fact]
    public void AuditOnlyStartsWithTheSourceAndShowsTargetStepsOnceATargetIsAdded()
    {
        var audit = ScenarioWorkflow.Build(ProjectScenario.AuditOnly, Collected);

        audit.Select(s => s.Step).ShouldBe([ScenarioStep.Source, ScenarioStep.SourceCollection, ScenarioStep.RunComparison,
            ScenarioStep.DecisionReview, ScenarioStep.Export, ScenarioStep.Target]);
        var target = Step(audit, ScenarioStep.Target);
        (target.State, target.Key, target.HintKey, target.Side).ShouldBe((ScenarioStepState.Optional, false, "StepHint_AuditOnly_Target", EnvironmentRole.Target));
        ScenarioWorkflow.Next(audit)!.Step.ShouldBe(ScenarioStep.Export);
        ScenarioWorkflow.Next(ScenarioWorkflow.Build(ProjectScenario.AuditOnly, Collected with { Exports = 1 })).ShouldBeNull();

        var later = ScenarioWorkflow.Build(ProjectScenario.AuditOnly, Collected with { PlannedTargets = 1 });
        Step(later, ScenarioStep.Target).State.ShouldBe(ScenarioStepState.Done);
        var collection = Step(later, ScenarioStep.TargetCollection);
        (collection.State, collection.HintKey).ShouldBe((ScenarioStepState.Waiting, "StepHint_TargetPlanned"));
        Step(later, ScenarioStep.MappingRules).State.ShouldBe(ScenarioStepState.Optional);
        Step(later, ScenarioStep.SourceTargetComparison).State.ShouldBe(ScenarioStepState.Optional);
        later.ShouldNotContain(s => s.Step == ScenarioStep.Populations);
    }

    [Fact]
    public void CarveOutLeadsWithTheDepartingPopulationAndFlagsResourcesSharedWithThoseWhoRemain()
    {
        var empty = ScenarioWorkflow.Build(ProjectScenario.CarveOut, Collected);
        var next = ScenarioWorkflow.Next(empty)!;
        (next.Step, next.HintKey).ShouldBe((ScenarioStep.Populations, "StepHint_CarveOut_Populations"));
        Step(empty, ScenarioStep.Dependencies).State.ShouldBe(ScenarioStepState.Waiting);
        empty.ShouldNotContain(s => s.Step == ScenarioStep.TargetCollection);
        Step(empty, ScenarioStep.Target).Key.ShouldBeFalse();

        var scoped = ScenarioWorkflow.Build(ProjectScenario.CarveOut, Collected with
        {
            Populations = 1,
            Included = 3,
            Excluded = 1,
            DependencyAnalyses = 1,
            SharedResources = 2,
            RemainingPrincipals = 5,
            Decisions = 4
        });
        var dependencies = Step(scoped, ScenarioStep.Dependencies);
        (dependencies.State, dependencies.Key, dependencies.HintKey).ShouldBe((ScenarioStepState.Attention, true, "StepHint_CarveOut_Dependencies"));
        dependencies.Counts.Single(c => c.Label == "Shared").Value.ShouldBe(2);
        dependencies.Counts.Single(c => c.Label == "Remaining").Value.ShouldBe(5);
        Step(scoped, ScenarioStep.ScopeDecisions).State.ShouldBe(ScenarioStepState.Done);
        ScenarioWorkflow.Next(scoped)!.Step.ShouldBe(ScenarioStep.Dependencies);
        Step(ScenarioWorkflow.Build(ProjectScenario.CarveOut, Collected with { Populations = 1, ToArbitrate = 2 }), ScenarioStep.ScopeDecisions)
            .State.ShouldBe(ScenarioStepState.Attention);
    }

    [Fact]
    public void CarveInRequiresAnExistingTargetAndActivatesDataComparisonOnlyWhenItExists()
    {
        var planned = ScenarioWorkflow.Build(ProjectScenario.CarveIn, Collected with { PlannedTargets = 1 });
        var target = Step(planned, ScenarioStep.Target);
        (target.State, target.HintKey).ShouldBe((ScenarioStepState.Attention, "StepHint_CarveIn_Target"));
        Step(planned, ScenarioStep.TargetCollection).State.ShouldBe(ScenarioStepState.Waiting);
        Step(planned, ScenarioStep.SourceCollection).HintKey.ShouldBe("StepHint_CarveIn_SourceCollection");
        ScenarioWorkflow.Next(planned)!.Step.ShouldBe(ScenarioStep.Target);

        var existing = ScenarioWorkflow.Build(ProjectScenario.CarveIn, Collected with { ExistingTargets = 1 });
        Step(existing, ScenarioStep.Target).State.ShouldBe(ScenarioStepState.Done);
        (Step(existing, ScenarioStep.TargetCollection).State, ScenarioWorkflow.Next(existing)!.Step)
            .ShouldBe((ScenarioStepState.ToDo, ScenarioStep.TargetCollection));
    }

    [Fact]
    public void ComparisonsDoNotTurnAbsentOrUnknownObjectsIntoFindingsAndCoverageGapsNeedVerification()
    {
        var compared = Collected with { ExistingTargets = 1, TargetSections = 2, MappingRules = 1, TargetComparisons = 1, Matched = 8, Absent = 3, Unknown = 2 };
        Step(ScenarioWorkflow.Build(ProjectScenario.TenantToTenant, compared), ScenarioStep.SourceTargetComparison).State
            .ShouldBe(ScenarioStepState.Done);
        var collisions = Step(ScenarioWorkflow.Build(ProjectScenario.TenantToTenant, compared with { Collisions = 1 }), ScenarioStep.SourceTargetComparison);
        (collisions.State, collisions.HintKey).ShouldBe((ScenarioStepState.Attention, "StepHint_TenantToTenant_SourceTargetComparison"));

        Step(ScenarioWorkflow.Build(ProjectScenario.TenantToTenant, compared with { SourceSectionsToVerify = 1 }), ScenarioStep.SourceCollection)
            .State.ShouldBe(ScenarioStepState.Attention);
        var review = Step(ScenarioWorkflow.Build(ProjectScenario.TenantToTenant, compared with { Decisions = 2, TargetToReview = 1 }),
            ScenarioStep.DecisionReview);
        (review.State, review.Side).ShouldBe((ScenarioStepState.Attention, EnvironmentRole.Target));
        Step(ScenarioWorkflow.Build(ProjectScenario.TenantToTenant, Collected), ScenarioStep.RunComparison).State.ShouldBe(ScenarioStepState.Waiting);
        Step(ScenarioWorkflow.Build(ProjectScenario.TenantToTenant, Collected with { ComparableEnvironments = 1 }), ScenarioStep.RunComparison)
            .State.ShouldBe(ScenarioStepState.Optional);
    }
}
