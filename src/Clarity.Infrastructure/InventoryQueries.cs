using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record InventoryFilter(EnvironmentRole Side, Guid? Environment = null, string? Domain = null, string? Site = null,
    string? Type = null, string? Status = null, Guid? Run = null, bool ConflictsOnly = false, bool Population = false,
    bool Unresolved = false, string? Volume = null, Guid? Section = null);
public sealed record InventoryRow(Guid Id, Guid EnvironmentId, string Environment, string? DisplayName, string Type, InventoryPresence Presence,
    string? Domain, string? Site, string? Upn, string? Smtp, bool? Enabled, DateTimeOffset? LastObservedAtUtc, string? TechnicalId,
    string? OperatingSystem, string? Sku, Guid? PresenceSectionId, bool UpnReported, bool SmtpReported, string? VolumeValue);

public sealed class InventoryQueries(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access)
{
    public sealed record RunOption(Guid Id, string Producer, DateTimeOffset Date);
    public sealed record Options(IReadOnlyList<string> Types, IReadOnlyList<RunOption> RecentRuns);
    public async Task<Options> OptionsAsync(Guid projectId, EnvironmentRole side, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var types = await Filter(db, project, new(side)).Select(i => i.Type).Distinct().Order().ToListAsync(token);
        var runs = await (from r in db.CollectionRuns
                          join e in db.Environments on new { r.WorkspaceId, Id = r.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                          where r.WorkspaceId == project.WorkspaceId && r.ProjectId == projectId && e.Role == side
                              && db.SectionRuns.Any(s => s.WorkspaceId == r.WorkspaceId && s.RunId == r.Id && s.PublishedAtUtc != null)
                          orderby r.CompletedAtUtc descending, r.Id
                          select new RunOption(r.Id, r.ProducerId, r.CompletedAtUtc)).Take(40).ToListAsync(token);
        return new(types, runs);
    }

    internal static IQueryable<InventoryObject> Filter(ClarityDbContext db, Project project, InventoryFilter filter, string search = "")
    {
        var workspace = project.WorkspaceId;
        var query = from i in db.InventoryObjects.AsNoTracking()
                    join e in db.Environments on new { i.WorkspaceId, Id = i.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                    where i.WorkspaceId == workspace && e.ProjectId == project.Id && e.Role == filter.Side
                    select i;
        if (!Enum.IsDefined(filter.Side)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        if (filter.Environment is { } environment) { query = query.Where(i => i.EnvironmentId == environment); }
        if (!string.IsNullOrEmpty(filter.Domain)) { query = query.Where(i => i.Domain == filter.Domain); }
        if (!string.IsNullOrEmpty(filter.Site)) { query = query.Where(i => i.Site == filter.Site); }
        if (!string.IsNullOrEmpty(filter.Type)) { query = query.Where(i => i.Type == filter.Type); }
        if (!string.IsNullOrEmpty(filter.Status))
        {
            query = filter.Status switch
            {
                "Enabled" => query.Where(i => i.Enabled == true),
                "Disabled" => query.Where(i => i.Enabled == false),
                _ when Enum.TryParse<InventoryPresence>(filter.Status, out var presence) && Enum.IsDefined(presence) => query.Where(i => i.Presence == presence),
                _ => throw new ArgumentException("Unknown inventory status.")
            };
        }
        if (filter.Run is { } run)
        {
            query = query.Where(i => (from o in db.ObjectObservations
                                      join s in db.SectionRuns on new { o.WorkspaceId, Id = o.SectionRunId } equals new { s.WorkspaceId, s.Id }
                                      where o.WorkspaceId == i.WorkspaceId && o.ObjectId == i.Id && s.RunId == run
                                      select o.Id).Any());
        }
        if (filter.ConflictsOnly) { query = query.Where(i => db.CurrentValues.Any(v => v.WorkspaceId == workspace && v.ObjectId == i.Id && v.Conflicting)); }
        if (filter.Population)
        {
            query = query.Where(i => i.Type == "user" && i.Presence == InventoryPresence.Observed
                && !db.MappingDecisions.Any(m => m.WorkspaceId == workspace && m.ProjectId == project.Id && m.AdObjectId == i.Id
                    && m.State == MappingState.Validated && db.InventoryObjects.Any(c => c.WorkspaceId == workspace && c.Id == m.EntraObjectId
                        && c.Presence == InventoryPresence.Observed)));
        }
        if (filter.Unresolved)
        {
            query = query.Where(i => db.InventoryRelationships.Any(e => e.WorkspaceId == workspace && e.EnvironmentId == i.EnvironmentId && e.IsCurrent
                && (e.FromObjectId == i.Id && e.ToObjectId == null || e.ToObjectId == i.Id && e.FromObjectId == null)));
        }
        if (filter.Section is { } section)
        {
            query = query.Where(i => db.ObjectObservations.Any(o => o.WorkspaceId == workspace && o.ObjectId == i.Id && o.SectionRunId == section));
        }
        if (filter.Volume is { } volume)
        {
            if (volume is not ("enabledUnits" or "consumedUnits")) { throw new ArgumentException("Invalid volume field."); }
            query = query.Where(i => i.Type == "subscribedSku" && i.Presence == InventoryPresence.Observed
                && db.CurrentValues.Any(v => v.WorkspaceId == workspace && v.ObjectId == i.Id && v.Field == volume && v.ValueJson != "null" && !v.Conflicting));
        }
        var term = search.Trim().ToUpperInvariant();
        if (term.Length != 0)
        {
            var literal = search.Trim();
            query = query.Where(i => i.SearchName != null && i.SearchName.StartsWith(term)
                || db.ExternalKeys.Any(k => k.WorkspaceId == workspace && k.ObjectId == i.Id && k.SearchId!.StartsWith(term))
                || db.AlternateKeyPeriods.Any(k => k.WorkspaceId == workspace && k.ObjectId == i.Id
                    && (k.NormalizedValue.StartsWith(term) || k.NormalizedValue.StartsWith(literal))));
        }
        return query;
    }

    public async Task<PageResult<InventoryRow>> PageAsync(Guid projectId, InventoryFilter filter, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var query = Filter(db, project, filter, page.Search);
        var count = await query.CountAsync(token);
        var ordered = (page.Sort, page.Descending) switch
        {
            (nameof(InventoryRow.Type), true) => query.OrderByDescending(i => i.Type),
            (nameof(InventoryRow.Type), false) => query.OrderBy(i => i.Type),
            (nameof(InventoryRow.LastObservedAtUtc), true) => query.OrderByDescending(i => i.LastObservedAtUtc),
            (nameof(InventoryRow.LastObservedAtUtc), false) => query.OrderBy(i => i.LastObservedAtUtc),
            (null or "" or nameof(InventoryRow.DisplayName), true) => query.OrderByDescending(i => i.DisplayName),
            (null or "" or nameof(InventoryRow.DisplayName), false) => query.OrderBy(i => i.DisplayName),
            _ => throw new ArgumentException("Unknown inventory sort column.")
        };
        var rows = await (from i in ordered.ThenBy(i => i.Id).Skip(page.Start).Take(page.Count)
                          select new InventoryRow(i.Id, i.EnvironmentId, db.Environments.Where(e => e.WorkspaceId == i.WorkspaceId && e.Id == i.EnvironmentId)
                              .Select(e => e.ExternalId).Single(), i.DisplayName, i.Type, i.Presence, i.Domain, i.Site, i.UserPrincipalName, i.Smtp, i.Enabled,
                              i.LastObservedAtUtc, db.ExternalKeys.Where(k => k.WorkspaceId == i.WorkspaceId && k.ObjectId == i.Id && k.IsPrimary)
                              .Select(k => k.ExternalId).FirstOrDefault(),
                              db.CurrentValues.Where(v => v.WorkspaceId == i.WorkspaceId && v.ObjectId == i.Id && v.Field == "operatingSystem").Select(v => v.ValueJson).FirstOrDefault(),
                              db.CurrentValues.Where(v => v.WorkspaceId == i.WorkspaceId && v.ObjectId == i.Id && v.Field == "skuPartNumber").Select(v => v.ValueJson).FirstOrDefault(),
                              i.PresenceSectionId,
                              db.CurrentValues.Any(v => v.WorkspaceId == i.WorkspaceId && v.ObjectId == i.Id && v.Field == "userPrincipalName"),
                              db.CurrentValues.Any(v => v.WorkspaceId == i.WorkspaceId && v.ObjectId == i.Id && v.Field == "mail"),
                              db.CurrentValues.Where(v => v.WorkspaceId == i.WorkspaceId && v.ObjectId == i.Id && v.Field == filter.Volume)
                                  .Select(v => v.ValueJson).FirstOrDefault())).ToListAsync(token);
        return new(rows, count);
    }
}
