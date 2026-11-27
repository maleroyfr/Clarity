using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record ScenarioOverview(ProjectScenario Scenario, ScenarioFacts Facts, IReadOnlyList<ScenarioStepView> Steps, ScenarioStepView? Next);

/// <summary>
/// Scenario-aware workflow of a project. One authorized read counts the project facts in the database; <see cref="ScenarioWorkflow"/> turns
/// them into ordered steps that open the existing screens. It reads only and never changes decisions.
/// </summary>
public sealed class ScenarioOverviewService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access)
{
    private const int LatestRows = 500;

    public async Task<ScenarioOverview> ReadAsync(Guid projectId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var ws = project.WorkspaceId;
        var environments = await db.Environments.AsNoTracking().Where(e => e.WorkspaceId == ws && e.ProjectId == project.Id)
            .Select(e => new { e.Id, e.Role, e.TargetState }).ToListAsync(token);
        var roles = environments.ToDictionary(e => e.Id, e => e.Role);

        var published = from s in db.SectionRuns
                        join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                        where s.WorkspaceId == ws && r.ProjectId == project.Id && s.PublishedAtUtc != null
                        select new { s.Id, r.EnvironmentId, s.CapabilityId, s.ScopeHash, s.Coverage, r.CompletedAtUtc, r.ExternalRunId, s.RunId };
        var units = await published.GroupBy(s => new { s.EnvironmentId, s.CapabilityId, s.ScopeHash })
            .Select(g => g.OrderByDescending(s => s.CompletedAtUtc).ThenByDescending(s => s.ExternalRunId).First()).ToListAsync(token);
        var runs = await published.Select(s => new { s.EnvironmentId, s.RunId }).Distinct().ToListAsync(token);

        var scopeCounts = await (from d in db.ScopeDecisions
                                 join e in db.Environments on new { d.WorkspaceId, Id = d.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                                 where d.WorkspaceId == ws && d.ProjectId == project.Id && e.Role == EnvironmentRole.Source
                                 group d by d.Value into g
                                 select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, token);
        var populations = await (from p in db.Populations
                                 join e in db.Environments on new { p.WorkspaceId, Id = p.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                                 where p.WorkspaceId == ws && p.ProjectId == project.Id && e.Role == EnvironmentRole.Source
                                 select p.Id).CountAsync(token);

        var recent = await (from a in db.DependencyAnalyses.AsNoTracking()
                            join e in db.Environments on new { a.WorkspaceId, Id = a.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                            where a.WorkspaceId == ws && a.ProjectId == project.Id && e.Role == EnvironmentRole.Source
                            orderby a.ComputedAtUtc descending, a.Id
                            select new { a.Id, a.EnvironmentId, a.Wave, a.UnresolvedLinks, a.RemainingPrincipals }).Take(LatestRows).ToListAsync(token);
        var analyses = recent.DistinctBy(a => (a.EnvironmentId, a.Wave)).ToList();
        var analysisIds = analyses.Select(a => a.Id).ToList();
        var shared = await db.DependencyResources.Where(r => r.WorkspaceId == ws && analysisIds.Contains(r.AnalysisId)
                && r.Classification == DependencyClassification.Shared)
            .Select(r => r.ObjectId).Distinct().CountAsync(token);
        var comparisons = (await db.TargetComparisons.AsNoTracking().Where(c => c.WorkspaceId == ws && c.ProjectId == project.Id)
                .OrderByDescending(c => c.ComputedAtUtc).ThenBy(c => c.Id)
                .Select(c => new { c.SourceEnvironmentId, c.TargetEnvironmentId, c.Matched, c.Ambiguous, c.Absent, c.Unknown, c.Collisions })
                .Take(LatestRows).ToListAsync(token))
            .DistinctBy(c => (c.SourceEnvironmentId, c.TargetEnvironmentId)).ToList();

        var flags = await (from f in db.ReviewFlags
                           join e in db.Environments on new { f.WorkspaceId, Id = f.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                           where f.WorkspaceId == ws && f.ProjectId == project.Id && f.Kind != DecisionKind.Mapping
                           group f by e.Role into g
                           select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, token);
        var mappingReviews = await (from m in db.MappingDecisions
                                    join e in db.Environments on new { m.WorkspaceId, Id = m.AdEnvironmentId } equals new { e.WorkspaceId, e.Id }
                                    where m.WorkspaceId == ws && m.ProjectId == project.Id && m.State == MappingState.ToReview
                                    group m by e.Role into g
                                    select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, token);
        var assessed = await db.Assessments.Where(a => a.WorkspaceId == ws && a.ProjectId == project.Id)
            .Select(a => new { a.Kind, a.ObjectId }).Distinct().CountAsync(token);
        var mapped = await db.MappingDecisions.CountAsync(m => m.WorkspaceId == ws && m.ProjectId == project.Id && m.State != MappingState.Proposed, token);

        var exports = await (from x in db.ExportJobs
                             join j in db.CollectionJobs on new { x.WorkspaceId, x.Id } equals new { j.WorkspaceId, j.Id }
                             where x.WorkspaceId == ws && x.ProjectId == project.Id
                             group j by j.State into g
                             select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, token);

        int Sections(EnvironmentRole role, bool toVerify) => units.Count(u => roles.GetValueOrDefault(u.EnvironmentId) == role
            && (!toVerify || u.Coverage is CoverageStatus.Partial or CoverageStatus.Failed));
        var facts = new ScenarioFacts
        {
            Sources = environments.Count(e => e.Role == EnvironmentRole.Source),
            ExistingTargets = environments.Count(e => e.Role == EnvironmentRole.Target && e.TargetState == TargetState.Existing),
            PlannedTargets = environments.Count(e => e.Role == EnvironmentRole.Target && e.TargetState == TargetState.Planned),
            SourceSections = Sections(EnvironmentRole.Source, false),
            SourceSectionsToVerify = Sections(EnvironmentRole.Source, true),
            TargetSections = Sections(EnvironmentRole.Target, false),
            TargetSectionsToVerify = Sections(EnvironmentRole.Target, true),
            ComparableEnvironments = runs.GroupBy(r => r.EnvironmentId).Count(g => g.Count() >= 2),
            Populations = populations,
            Included = scopeCounts.GetValueOrDefault(ScopeValue.Include),
            Excluded = scopeCounts.GetValueOrDefault(ScopeValue.Exclude),
            ToArbitrate = scopeCounts.GetValueOrDefault(ScopeValue.ToArbitrate),
            DependencyAnalyses = analyses.Count,
            SharedResources = shared,
            UnresolvedLinks = analyses.Sum(a => a.UnresolvedLinks),
            RemainingPrincipals = analyses.DistinctBy(a => a.EnvironmentId).Sum(a => a.RemainingPrincipals),
            RunComparisons = await db.RunComparisons.CountAsync(c => c.WorkspaceId == ws && c.ProjectId == project.Id, token),
            MappingRules = await db.TargetMappingRules.CountAsync(r => r.WorkspaceId == ws && r.ProjectId == project.Id && r.RetiredAtUtc == null, token),
            TargetComparisons = comparisons.Count,
            Matched = comparisons.Sum(c => c.Matched),
            Ambiguous = comparisons.Sum(c => c.Ambiguous),
            Absent = comparisons.Sum(c => c.Absent),
            Unknown = comparisons.Sum(c => c.Unknown),
            Collisions = comparisons.Sum(c => c.Collisions),
            Decisions = scopeCounts.Values.Sum() + assessed + mapped,
            SourceToReview = flags.GetValueOrDefault(EnvironmentRole.Source) + mappingReviews.GetValueOrDefault(EnvironmentRole.Source),
            TargetToReview = flags.GetValueOrDefault(EnvironmentRole.Target) + mappingReviews.GetValueOrDefault(EnvironmentRole.Target),
            Exports = exports.GetValueOrDefault(CollectionRunState.Completed),
            FailedExports = exports.GetValueOrDefault(CollectionRunState.Failed),
            PendingExports = exports.GetValueOrDefault(CollectionRunState.Pending) + exports.GetValueOrDefault(CollectionRunState.Running)
        };
        var steps = ScenarioWorkflow.Build(project.Scenario, facts);
        return new(project.Scenario, facts, steps, ScenarioWorkflow.Next(steps));
    }
}
