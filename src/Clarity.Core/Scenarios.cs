namespace Clarity.Core;

/// <summary>A step of the scenario workflow; each one opens an existing working feature.</summary>
public enum ScenarioStep
{
    Source, SourceCollection, Target, TargetCollection, Populations, ScopeDecisions, Dependencies, RunComparison, MappingRules,
    SourceTargetComparison, DecisionReview, Export
}

/// <summary>
/// Done: evidence exists. Attention: evidence exists with points to review. ToDo: a key step not started. Waiting: a prerequisite is
/// missing. Optional: a supporting step not started.
/// </summary>
public enum ScenarioStepState { Done, Attention, ToDo, Waiting, Optional }

/// <summary>
/// Project facts counted in the database for the scenario workflow. Scoping facts concern the source side; the target side is described by
/// its declared state and its collections. Comparison facts use the latest comparison of each environment pair.
/// </summary>
public sealed record ScenarioFacts
{
    public int Sources { get; init; }
    public int ExistingTargets { get; init; }
    public int PlannedTargets { get; init; }
    public int SourceSections { get; init; }
    public int SourceSectionsToVerify { get; init; }
    public int TargetSections { get; init; }
    public int TargetSectionsToVerify { get; init; }
    public int ComparableEnvironments { get; init; }
    public int Populations { get; init; }
    public int Included { get; init; }
    public int Excluded { get; init; }
    public int ToArbitrate { get; init; }
    public int DependencyAnalyses { get; init; }
    public int SharedResources { get; init; }
    public int UnresolvedLinks { get; init; }
    public int RemainingPrincipals { get; init; }
    public int RunComparisons { get; init; }
    public int MappingRules { get; init; }
    public int TargetComparisons { get; init; }
    public int Matched { get; init; }
    public int Ambiguous { get; init; }
    public int Absent { get; init; }
    public int Unknown { get; init; }
    public int Collisions { get; init; }
    public int Decisions { get; init; }
    public int SourceToReview { get; init; }
    public int TargetToReview { get; init; }
    public int Exports { get; init; }
    public int FailedExports { get; init; }
    public int PendingExports { get; init; }
    public int Targets => ExistingTargets + PlannedTargets;
}

/// <summary>A counter shown with a step; <see cref="Label"/> is a localization suffix.</summary>
public sealed record ScenarioCount(string Label, int Value);

/// <summary>
/// One workflow step. Key steps carry the scenario objective; supporting steps remain available. <see cref="HintKey"/> is a localization key
/// and <see cref="Side"/> the side whose screens the step opens.
/// </summary>
public sealed record ScenarioStepView(ScenarioStep Step, ScenarioStepState State, bool Key, string HintKey, EnvironmentRole Side,
    IReadOnlyList<ScenarioCount> Counts);

/// <summary>Builds the ordered steps of a scenario from project facts. It never changes data or decisions.</summary>
public static class ScenarioWorkflow
{
    private static readonly ScenarioStep[] TargetDependent = [ScenarioStep.TargetCollection, ScenarioStep.MappingRules, ScenarioStep.SourceTargetComparison];
    private static readonly ScenarioStep[] Scoping = [ScenarioStep.Populations, ScenarioStep.ScopeDecisions, ScenarioStep.Dependencies];

    private static readonly Dictionary<ProjectScenario, (ScenarioStep Step, bool Key)[]> Plans = new()
    {
        [ProjectScenario.TenantToTenant] =
        [
            (ScenarioStep.Source, true), (ScenarioStep.SourceCollection, true), (ScenarioStep.Target, true), (ScenarioStep.TargetCollection, true),
            (ScenarioStep.MappingRules, true), (ScenarioStep.SourceTargetComparison, true), (ScenarioStep.Populations, false),
            (ScenarioStep.ScopeDecisions, false), (ScenarioStep.Dependencies, false), (ScenarioStep.RunComparison, false),
            (ScenarioStep.DecisionReview, true), (ScenarioStep.Export, true)
        ],
        [ProjectScenario.CarveOut] =
        [
            (ScenarioStep.Source, true), (ScenarioStep.SourceCollection, true), (ScenarioStep.Populations, true), (ScenarioStep.ScopeDecisions, true),
            (ScenarioStep.Dependencies, true), (ScenarioStep.RunComparison, false), (ScenarioStep.Target, false), (ScenarioStep.TargetCollection, false),
            (ScenarioStep.MappingRules, false), (ScenarioStep.SourceTargetComparison, false), (ScenarioStep.DecisionReview, true),
            (ScenarioStep.Export, true)
        ],
        [ProjectScenario.CarveIn] =
        [
            (ScenarioStep.Source, true), (ScenarioStep.SourceCollection, true), (ScenarioStep.Target, true), (ScenarioStep.TargetCollection, true),
            (ScenarioStep.MappingRules, true), (ScenarioStep.SourceTargetComparison, true), (ScenarioStep.Populations, true),
            (ScenarioStep.ScopeDecisions, true), (ScenarioStep.Dependencies, true), (ScenarioStep.RunComparison, false),
            (ScenarioStep.DecisionReview, true), (ScenarioStep.Export, true)
        ],
        [ProjectScenario.AuditOnly] =
        [
            (ScenarioStep.Source, true), (ScenarioStep.SourceCollection, true), (ScenarioStep.RunComparison, false),
            (ScenarioStep.Populations, false), (ScenarioStep.ScopeDecisions, false), (ScenarioStep.Dependencies, false),
            (ScenarioStep.DecisionReview, false), (ScenarioStep.Export, true), (ScenarioStep.Target, false), (ScenarioStep.TargetCollection, false),
            (ScenarioStep.MappingRules, false), (ScenarioStep.SourceTargetComparison, false)
        ]
    };

    private static readonly HashSet<(ProjectScenario, ScenarioStep)> SpecificHints =
    [
        (ProjectScenario.TenantToTenant, ScenarioStep.TargetCollection), (ProjectScenario.TenantToTenant, ScenarioStep.SourceTargetComparison),
        (ProjectScenario.CarveOut, ScenarioStep.Populations), (ProjectScenario.CarveOut, ScenarioStep.Dependencies),
        (ProjectScenario.CarveIn, ScenarioStep.SourceCollection), (ProjectScenario.CarveIn, ScenarioStep.Target),
        (ProjectScenario.CarveIn, ScenarioStep.SourceTargetComparison), (ProjectScenario.CarveIn, ScenarioStep.Dependencies),
        (ProjectScenario.AuditOnly, ScenarioStep.Target), (ProjectScenario.AuditOnly, ScenarioStep.Export)
    ];

    public static IReadOnlyList<ScenarioStepView> Build(ProjectScenario scenario, ScenarioFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!Plans.TryGetValue(scenario, out var plan)) { throw new ArgumentOutOfRangeException(nameof(scenario)); }
        var scopingData = facts.Populations > 0 || facts.Included + facts.Excluded + facts.ToArbitrate > 0 || facts.DependencyAnalyses > 0;
        return plan
            .Where(p => p.Key || facts.Targets > 0 || !TargetDependent.Contains(p.Step))
            .Where(p => scenario != ProjectScenario.AuditOnly || scopingData || !Scoping.Contains(p.Step))
            .Select(p => View(scenario, p.Step, p.Key, facts)).ToList();
    }

    /// <summary>The first key step that requires an action, or null when key steps are done or waiting for a prerequisite.</summary>
    public static ScenarioStepView? Next(IEnumerable<ScenarioStepView> steps) =>
        steps.FirstOrDefault(s => s.Key && s.State is ScenarioStepState.ToDo or ScenarioStepState.Attention);

    private static ScenarioStepView View(ProjectScenario scenario, ScenarioStep step, bool key, ScenarioFacts f)
    {
        var notStarted = key ? ScenarioStepState.ToDo : ScenarioStepState.Optional;
        var sourceReady = f.Sources > 0 && f.SourceSections > 0;
        var toReview = f.SourceToReview + f.TargetToReview;
        var (state, counts) = step switch
        {
            ScenarioStep.Source => (f.Sources > 0 ? ScenarioStepState.Done : ScenarioStepState.ToDo, Counts(("Sources", f.Sources))),
            ScenarioStep.SourceCollection => (f.Sources == 0 ? ScenarioStepState.Waiting : f.SourceSections == 0 ? notStarted
                : f.SourceSectionsToVerify > 0 ? ScenarioStepState.Attention : ScenarioStepState.Done,
                Counts(("Sections", f.SourceSections), ("SectionsToVerify", f.SourceSectionsToVerify))),
            ScenarioStep.Target => (f.Targets == 0 ? notStarted
                : scenario == ProjectScenario.CarveIn && f.ExistingTargets == 0 ? ScenarioStepState.Attention : ScenarioStepState.Done,
                Counts(("Existing", f.ExistingTargets), ("Planned", f.PlannedTargets))),
            ScenarioStep.TargetCollection => (f.ExistingTargets == 0 ? ScenarioStepState.Waiting : f.TargetSections == 0 ? notStarted
                : f.TargetSectionsToVerify > 0 ? ScenarioStepState.Attention : ScenarioStepState.Done,
                Counts(("Sections", f.TargetSections), ("SectionsToVerify", f.TargetSectionsToVerify))),
            ScenarioStep.Populations => (f.Populations > 0 ? ScenarioStepState.Done : sourceReady ? notStarted : ScenarioStepState.Waiting,
                Counts(("Populations", f.Populations))),
            ScenarioStep.ScopeDecisions => (f.Included + f.Excluded + f.ToArbitrate == 0
                    ? f.Populations == 0 ? ScenarioStepState.Waiting : notStarted
                    : f.ToArbitrate > 0 ? ScenarioStepState.Attention : ScenarioStepState.Done,
                Counts(("Include", f.Included), ("Exclude", f.Excluded), ("ToArbitrate", f.ToArbitrate))),
            ScenarioStep.Dependencies => (f.DependencyAnalyses > 0
                    ? f.SharedResources > 0 || f.UnresolvedLinks > 0 ? ScenarioStepState.Attention : ScenarioStepState.Done
                    : f.Included == 0 ? ScenarioStepState.Waiting : notStarted,
                Counts(("Analyses", f.DependencyAnalyses), ("Shared", f.SharedResources), ("UnresolvedLinks", f.UnresolvedLinks),
                    ("Remaining", f.RemainingPrincipals))),
            ScenarioStep.RunComparison => (f.RunComparisons > 0 ? ScenarioStepState.Done
                    : f.ComparableEnvironments == 0 ? ScenarioStepState.Waiting : notStarted,
                Counts(("Comparisons", f.RunComparisons))),
            ScenarioStep.MappingRules => (f.MappingRules > 0 ? ScenarioStepState.Done : f.Targets == 0 ? ScenarioStepState.Waiting : notStarted,
                Counts(("Rules", f.MappingRules))),
            ScenarioStep.SourceTargetComparison => (f.TargetComparisons > 0
                    ? f.Collisions > 0 || f.Ambiguous > 0 ? ScenarioStepState.Attention : ScenarioStepState.Done
                    : f.Targets == 0 || !sourceReady ? ScenarioStepState.Waiting : notStarted,
                Counts(("Comparisons", f.TargetComparisons), ("Matched", f.Matched), ("Absent", f.Absent), ("Ambiguous", f.Ambiguous),
                    ("Unknown", f.Unknown), ("Collisions", f.Collisions))),
            ScenarioStep.DecisionReview => (toReview > 0 ? ScenarioStepState.Attention
                    : f.Decisions == 0 ? ScenarioStepState.Waiting : ScenarioStepState.Done,
                Counts(("Decisions", f.Decisions), ("ToReview", toReview))),
            ScenarioStep.Export => (f.Exports > 0 ? ScenarioStepState.Done : f.FailedExports > 0 ? ScenarioStepState.Attention
                    : sourceReady ? notStarted : ScenarioStepState.Waiting,
                Counts(("Exports", f.Exports), ("FailedExports", f.FailedExports), ("PendingExports", f.PendingExports))),
            _ => throw new ArgumentOutOfRangeException(nameof(step))
        };
        var side = step is ScenarioStep.Target or ScenarioStep.TargetCollection
            || (step == ScenarioStep.DecisionReview && f.SourceToReview == 0 && f.TargetToReview > 0)
            ? EnvironmentRole.Target : EnvironmentRole.Source;
        return new(step, state, key, Hint(scenario, step, f), side, counts);
    }

    private static string Hint(ProjectScenario scenario, ScenarioStep step, ScenarioFacts f) =>
        step == ScenarioStep.TargetCollection && f.Targets > 0 && f.ExistingTargets == 0 ? "StepHint_TargetPlanned"
        : SpecificHints.Contains((scenario, step)) ? $"StepHint_{scenario}_{step}" : $"StepHint_{step}";

    private static List<ScenarioCount> Counts(params (string Label, int Value)[] values) =>
        values.Select(v => new ScenarioCount(v.Label, v.Value)).ToList();
}
