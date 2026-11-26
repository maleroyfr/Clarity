using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record InventoryMetric(string Label, decimal? Amount, InventoryFilter Filter, int? KnownResources = null, int? ObservedResources = null);
public sealed record CoverageUnit(Guid SectionId, Guid EnvironmentId, string Environment, string Capability, string ScopeJson, CoverageStatus Status,
    DateTimeOffset Date, Guid RunId);
public sealed record ChangedObject(Guid Id, string Label, DateTimeOffset? Changed);
public sealed record OverviewData(IReadOnlyList<InventoryMetric> Metrics, int ReviewCount, int PendingLinks, IReadOnlyList<CoverageUnit> Units,
    IReadOnlyList<ChangedObject> Changes, bool HasObservations);

public sealed class InventoryOverview(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access)
{
    public async Task<OverviewData> ReadAsync(Guid projectId, EnvironmentRole side, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var sections = from s in db.SectionRuns
                       join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                       join e in db.Environments on new { r.WorkspaceId, Id = r.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                       where s.WorkspaceId == project.WorkspaceId && r.ProjectId == project.Id && e.Role == side && s.PublishedAtUtc != null
                       select new { s.Id, r.EnvironmentId, e.ExternalId, s.CapabilityId, s.ScopeHash, s.ScopeJson, s.Coverage, r.CompletedAtUtc, r.ExternalRunId, s.RunId, s.ObjectCount };
        var units = await sections.GroupBy(s => new { s.EnvironmentId, s.CapabilityId, s.ScopeHash })
            .Select(g => g.OrderByDescending(s => s.CompletedAtUtc).ThenByDescending(s => s.ExternalRunId).First()).ToListAsync(token);
        var list = units.OrderByDescending(s => s.CompletedAtUtc).ThenBy(s => s.CapabilityId, StringComparer.Ordinal)
            .Select(s => new CoverageUnit(s.Id, s.EnvironmentId, s.ExternalId, s.CapabilityId, s.ScopeJson, s.Coverage, s.CompletedAtUtc, s.RunId)).ToList();
        var metrics = new List<InventoryMetric>();
        IQueryable<InventoryObject> Items(InventoryFilter f) => InventoryQueries.Filter(db, project, f);
        var population = new InventoryFilter(side, Population: true);
        var populationCount = await Items(population).CountAsync(token);
        var scopeKnown = units.Any(u => u.CapabilityId is "ad.users" or "entra.users" && u.Coverage == CoverageStatus.Complete);
        metrics.Add(new("ObservedPopulation", populationCount > 0 || scopeKnown ? populationCount : null, population));
        foreach (var filter in new[] { new InventoryFilter(side, ConflictsOnly: true), new InventoryFilter(side, Unresolved: true) })
        {
            metrics.Add(new(filter.ConflictsOnly ? "ConflictingObjects" : "ObjectsWithPendingLinks", await Items(filter).CountAsync(token), filter));
        }
        var skus = await Items(new(side, Type: "subscribedSku", Status: "Observed")).CountAsync(token);
        foreach (var field in new[] { "enabledUnits", "consumedUnits" })
        {
            var filter = new InventoryFilter(side, Type: "subscribedSku", Volume: field);
            var values = from i in Items(filter)
                         join v in db.CurrentValues on new { i.WorkspaceId, ObjectId = i.Id } equals new { v.WorkspaceId, v.ObjectId }
                         where v.Field == field
                         select v.ValueJson;
            var count = await values.CountAsync(token);
            decimal? sum = count == 0 ? null : await values.SumAsync(v => Convert.ToDecimal(v), token);
            metrics.Add(new(field, sum, filter, count, skus));
        }
        var review = await (from m in db.MappingDecisions
                            join e in db.Environments on new { m.WorkspaceId, Id = m.AdEnvironmentId } equals new { e.WorkspaceId, e.Id }
                            where m.WorkspaceId == project.WorkspaceId && m.ProjectId == projectId && e.Role == side
                                && (m.State == MappingState.Proposed || m.State == MappingState.ToReview)
                            select m.Id).CountAsync(token);
        var changes = await Items(new(side)).Where(i => i.LastChangedAtUtc != null)
            .OrderByDescending(i => i.LastChangedAtUtc).ThenBy(i => i.Id).Take(8)
            .Select(i => new ChangedObject(i.Id, i.DisplayName ?? i.UserPrincipalName ?? i.Id.ToString(), i.LastChangedAtUtc)).ToListAsync(token);
        var pending = await InventoryGraph.Pending(db, project, side).CountAsync(token);
        return new(metrics, review, pending, list, changes, await Items(new(side)).AnyAsync(token));
    }
}
