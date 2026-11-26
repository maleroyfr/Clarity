using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record SheetField(string Name, string Json, Guid ObservationId, bool Conflicting);
public sealed record SheetAlias(string Namespace, string Value, DateTimeOffset From, DateTimeOffset? Until, Guid ObservationId);
public sealed record SheetAssociation(Guid DecisionId, Guid OtherId, string? OtherName, MappingState State, AssociationRule Rule);
public sealed record ObjectSheetData(Guid Id, string Type, string? Name, string Environment, EnvironmentRole Side, InventoryPresence Presence,
    Guid? PresenceSection, IReadOnlyList<ExternalReference> Keys, IReadOnlyList<SheetField> Fields, IReadOnlyList<SheetAssociation> Associations);
public sealed record SheetEdge(Guid Id, string Type, RelationshipOrigin Origin, bool Outgoing, Guid? OtherId, string OtherLabel,
    EndpointResolution Resolution, bool Current);
public sealed record SheetObservation(Guid Id, string Producer, string Capability, DateTimeOffset ObservedAtUtc,
    CoverageStatus Coverage, IReadOnlyDictionary<string, JsonElement> Attributes);

public sealed class InventorySheet(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access)
{
    private static IQueryable<InventoryObject> Items(ClarityDbContext db, Project project) =>
        from i in db.InventoryObjects.AsNoTracking()
        join e in db.Environments on new { i.WorkspaceId, Id = i.EnvironmentId } equals new { e.WorkspaceId, e.Id }
        where i.WorkspaceId == project.WorkspaceId && e.ProjectId == project.Id
        select i;

    public async Task<ObjectSheetData> ReadAsync(Guid projectId, Guid id, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var item = await Items(db, project).SingleOrDefaultAsync(i => i.Id == id, token) ?? throw new AccessDeniedException();
        var environment = await db.Environments.AsNoTracking().SingleAsync(e => e.WorkspaceId == project.WorkspaceId
            && e.ProjectId == projectId && e.Id == item.EnvironmentId, token);
        var keys = await db.ExternalKeys.Where(k => k.WorkspaceId == project.WorkspaceId && k.ObjectId == id && k.IsPrimary)
            .Select(k => new ExternalReference(k.Type, k.Namespace, k.ExternalId)).ToListAsync(token);
        var fields = await db.CurrentValues.Where(v => v.WorkspaceId == project.WorkspaceId && v.ObjectId == id).OrderBy(v => v.Field)
            .Select(v => new SheetField(v.Field, v.ValueJson, v.ObservationId, v.Conflicting)).ToListAsync(token);
        var associations = await (from m in db.MappingDecisions
                                  from i in Items(db, project)
                                  where m.WorkspaceId == project.WorkspaceId && m.ProjectId == projectId &&
                                     (m.AdObjectId == id && m.EntraObjectId == i.Id || m.EntraObjectId == id && m.AdObjectId == i.Id)
                                  orderby i.DisplayName, i.Id
                                  select new SheetAssociation(m.Id, i.Id, i.DisplayName ?? i.UserPrincipalName, m.State, m.Rule)).ToListAsync(token);
        return new(id, item.Type, item.DisplayName, environment.ExternalId, environment.Role, item.Presence,
            item.PresenceSectionId, keys, fields, associations);
    }

    public async Task<PageResult<SheetEdge>> EdgesAsync(Guid projectId, Guid id, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var item = await Items(db, project).SingleOrDefaultAsync(i => i.Id == id, token) ?? throw new AccessDeniedException();
        var edges = db.InventoryRelationships.Where(e => e.WorkspaceId == project.WorkspaceId && e.EnvironmentId == item.EnvironmentId
            && (e.FromObjectId == id || e.ToObjectId == id));
        var count = await edges.CountAsync(token);
        var pageRows = await (from e in edges.OrderByDescending(e => e.IsCurrent).ThenBy(e => e.RelationType).ThenBy(e => e.Id).Skip(page.Start).Take(page.Count)
                              let outgoing = e.FromObjectId == id
                              let other = outgoing ? e.ToObjectId : e.FromObjectId
                              select new
                              {
                                  e.Id,
                                  e.RelationType,
                                  e.Origin,
                                  e.IsCurrent,
                                  Outgoing = outgoing,
                                  Other = other,
                                  Name = db.InventoryObjects.Where(i => i.WorkspaceId == e.WorkspaceId && i.EnvironmentId == e.EnvironmentId && i.Id == other)
                                      .Select(i => i.DisplayName ?? i.UserPrincipalName).FirstOrDefault(),
                                  Reference = outgoing ? e.ToJson : e.FromJson,
                                  Method = outgoing ? e.ToMethod : e.FromMethod
                              }).ToListAsync(token);
        return new(pageRows.Select(e => new SheetEdge(e.Id, e.RelationType, e.Origin, e.Outgoing, e.Other, e.Name ??
            JsonSerializer.Deserialize<ExternalReference>(e.Reference, ContractJson.Options)!.ExternalId, e.Method, e.IsCurrent)).ToList(), count);
    }

    public async Task<PageResult<SheetObservation>> HistoryAsync(Guid projectId, Guid id, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        if (!await Items(db, project).AnyAsync(i => i.Id == id, token)) { throw new AccessDeniedException(); }
        var query = from o in db.ObjectObservations
                    join s in db.SectionRuns on new { o.WorkspaceId, Id = o.SectionRunId } equals new { s.WorkspaceId, s.Id }
                    join r in db.CollectionRuns on new { s.WorkspaceId, Id = s.RunId } equals new { r.WorkspaceId, r.Id }
                    where r.ProjectId == projectId && o.WorkspaceId == project.WorkspaceId && o.ObjectId == id && s.PublishedAtUtc != null
                    select new { o.Id, o.AttributesJson, o.ObservedAtUtc, s.Coverage, s.CapabilityId, r.ProducerId };
        var count = await query.CountAsync(token);
        var sorted = page.Sort != null && !page.Descending ? query.OrderBy(x => x.ObservedAtUtc) : query.OrderByDescending(x => x.ObservedAtUtc);
        var rows = await sorted.ThenBy(x => x.Id).Skip(page.Start).Take(page.Count).ToListAsync(token);
        return new(rows.Select(o => new SheetObservation(o.Id, o.ProducerId, o.CapabilityId, o.ObservedAtUtc, o.Coverage,
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(o.AttributesJson)!)).ToList(), count);
    }

    public async Task<PageResult<SheetAlias>> AliasesAsync(Guid projectId, Guid id, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        if (!await Items(db, project).AnyAsync(i => i.Id == id, token)) { throw new AccessDeniedException(); }
        var query = db.AlternateKeyPeriods.Where(k => k.WorkspaceId == project.WorkspaceId && k.ObjectId == id);
        var count = await query.CountAsync(token);
        return new(await query.OrderByDescending(k => k.ValidFromUtc).ThenBy(k => k.Namespace).ThenBy(k => k.Id)
            .Skip(page.Start).Take(page.Count).Select(k => new SheetAlias(k.Namespace, k.OriginalValue, k.ValidFromUtc, k.ValidUntilUtc, k.ObservationId))
            .ToListAsync(token), count);
    }
}
