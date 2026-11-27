using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record DependencyFamilyCount(DependencyFamily Family, int Resources, int Shared);
public sealed record DependencySummary(Guid Id, Guid EnvironmentId, string Environment, string? Wave, DateTimeOffset ComputedAtUtc, Guid ActorObjectId,
    int MigratingPrincipals, int OtherWavePrincipals, int ArbitratePrincipals, int RemainingPrincipals, int Resources, int SharedResources,
    int UnresolvedLinks, long KnownVolumeBytes, long SharedVolumeBytes, int UnknownVolumeResources,
    IReadOnlyList<DependencyFamilyCoverage> Coverage, IReadOnlyList<DependencyFamilyCount> Families);
public sealed record DependencyFilter(DependencyFamily? Family = null, DependencyClassification? Classification = null);
public sealed record DependencyResourceRow(Guid ObjectId, DependencyFamily Family, string Type, string? DisplayName, DependencyClassification Classification,
    int Migrating, int OtherWave, int Arbitrate, int Remaining, int Routes, bool VolumeApplies, long? VolumeBytes, int UnresolvedLinks,
    bool MembersNotCollected, bool AllUsers, ScopeValue? Decision, string? DecisionWave);
public sealed record DependencyLinkRow(DependencyLinkKind Kind, Guid ObjectId, string? Label, string Type, DependencySide? Side, string Route,
    Guid? ViaObjectId, string? Via, Guid? RelationshipId);

/// <summary>
/// Computes which collective resources the migrating population depends on, and which of them are shared with principals that
/// stay behind. Results are a replaceable snapshot per environment and wave; they never alter consultant decisions.
/// </summary>
public sealed class DependencyService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope, DiscoveryCatalog catalog)
{
    private const string Analyzed = "Scope.DependenciesAnalyzed";
    private static readonly string[] Volumes = ["storageUsedBytes", "quotaUsedBytes", "storageUsedMegabytes", "sizeBytes", "archiveSizeBytes"];
    private static readonly string[] TargetFields = ["targetType", "membershipType"];

    public async Task<Guid> AnalyzeAsync(Guid projectId, Guid environmentId, string? wave, Guid operationId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        var label = ScopeRules.Wave(wave);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var ws = project.WorkspaceId;
        var replay = await db.DependencyAnalyses.AsNoTracking().SingleOrDefaultAsync(a => a.WorkspaceId == ws && a.OperationId == operationId, token);
        if (replay is not null)
        {
            if (replay.ProjectId != project.Id || replay.EnvironmentId != environmentId || replay.Wave != label
                || replay.ActorTenantId != scope.Identity.TenantId || replay.ActorObjectId != scope.Identity.ObjectId) { throw new ConflictException(); }
            return replay.Id;
        }
        if (await db.AuditEvents.AnyAsync(a => a.WorkspaceId == ws && a.ResourceId == operationId, token)) { throw new ConflictException(); }
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, token);
        var env = environment.Id;

        var edgeQuery = db.InventoryRelationships.AsNoTracking().Where(e => e.WorkspaceId == ws && e.EnvironmentId == env && e.IsCurrent
            && DependencyEngine.Relations.Contains(e.RelationType));
        if (await edgeQuery.CountAsync(token) > DependencyRules.MaxEdges)
        {
            throw new ArgumentException("The environment has too many relations for one dependency analysis.");
        }
        var edges = await edgeQuery.OrderBy(e => e.Id).Select(e => new DependencyEdge(e.Id, e.RelationType, e.FromObjectId, e.ToObjectId)).ToListAsync(token);
        var nodes = await db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == ws && i.EnvironmentId == env)
            .Select(i => new DependencyNode(i.Id, i.Type, i.Presence == InventoryPresence.Deleted)).ToListAsync(token);
        var decisions = await db.ScopeDecisions.AsNoTracking().Where(d => d.WorkspaceId == ws && d.ProjectId == project.Id && d.EnvironmentId == env)
            .Select(d => new { d.ObjectId, d.Value, d.Wave }).ToDictionaryAsync(d => d.ObjectId, token);
        var sides = await SidesAsync(db, project, env, label, nodes, decisions.ToDictionary(d => d.Key, d => Side(d.Value.Value, d.Value.Wave, label)), token);
        var values = await (from v in db.CurrentValues.AsNoTracking()
                            join i in db.InventoryObjects on new { v.WorkspaceId, Id = v.ObjectId } equals new { i.WorkspaceId, i.Id }
                            where v.WorkspaceId == ws && i.EnvironmentId == env && TargetFields.Contains(v.Field) && !v.Conflicting
                            select new { v.ObjectId, v.Field, v.ValueJson }).ToListAsync(token);
        string? Text(string json) => JsonSerializer.Deserialize<JsonElement>(json) is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
        var targets = values.Where(v => v.Field == "targetType").ToDictionary(v => v.ObjectId, v => Text(v.ValueJson));
        var result = DependencyEngine.Analyze(new(nodes, edges, sides,
            targets.Where(t => t.Value is "#microsoft.graph.allLicensedUsersAssignmentTarget" or "#microsoft.graph.allDevicesAssignmentTarget")
                .Select(t => t.Key).ToHashSet(),
            targets.Where(t => t.Value == "#microsoft.graph.exclusionGroupAssignmentTarget").Select(t => t.Key).ToHashSet(),
            values.Where(v => v.Field == "membershipType").ToDictionary(v => v.ObjectId, v => Text(v.ValueJson) ?? "")));

        var ids = result.Findings.Select(f => f.ObjectId).ToList();
        var names = new Dictionary<Guid, string?>();
        var volumes = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var chunk in ids.Chunk(500))
        {
            foreach (var item in await db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == ws && chunk.Contains(i.Id))
                .Select(i => new { i.Id, Name = i.DisplayName ?? i.UserPrincipalName }).ToListAsync(token))
            { names[item.Id] = item.Name; }
            foreach (var value in await db.CurrentValues.AsNoTracking().Where(v => v.WorkspaceId == ws && chunk.Contains(v.ObjectId)
                && Volumes.Contains(v.Field) && !v.Conflicting).Select(v => new { v.ObjectId, v.Field, v.ValueJson }).ToListAsync(token))
            {
                if (!volumes.TryGetValue(value.ObjectId, out var fields)) { volumes[value.ObjectId] = fields = []; }
                fields[value.Field] = value.ValueJson;
            }
        }

        var sections = (await ProjectedSection.LoadAsync(db, ws, env, token))
            .GroupBy(s => s.Section.CapabilityId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Completed).ThenByDescending(s => s.ExternalRunId).First(), StringComparer.Ordinal);
        var kind = environment.Kind.ToString();
        var applicable = catalog.Families.Where(f => f.EnvironmentKind == kind).SelectMany(f => f.Sections).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var coverage = Enum.GetValues<DependencyFamily>().Select(family =>
        {
            var found = result.Findings.Where(f => f.Family == family).ToList();
            var list = DependencyRules.Sections[family].Where(applicable.ContainsKey).Select(id => sections.TryGetValue(id, out var s)
                ? new DependencySectionCoverage(id, s.Section.Coverage, s.Completed,
                    JsonSerializer.Deserialize<CollectionScope>(s.Section.ScopeJson, ContractJson.Options)?.Kind is not ("tenant" or "domain"))
                : new DependencySectionCoverage(id, null, null, !Tenantwide(applicable[id]))).ToList();
            var unresolved = found.Sum(f => f.Unresolved);
            var partial = found.Count(f => f.MembersNotCollected) + result.Gaps.Count(g => g.Family == family);
            return new DependencyFamilyCoverage(family, DependencyRules.Status(list, unresolved, partial), list, unresolved, partial);
        }).ToList();

        var analysisId = Guid.NewGuid();
        var resources = result.Findings.Select(f =>
        {
            var applies = f.Family is DependencyFamily.Sites or DependencyFamily.Mailboxes;
            var decision = decisions.GetValueOrDefault(f.ObjectId);
            var name = names.GetValueOrDefault(f.ObjectId);
            return new DependencyResource
            {
                WorkspaceId = ws,
                AnalysisId = analysisId,
                EnvironmentId = env,
                ObjectId = f.ObjectId,
                Family = f.Family,
                Type = f.Type,
                DisplayName = name is { Length: > 400 } ? name[..400] : name,
                Classification = f.Classification,
                MigratingCount = f.Migrating,
                OtherWaveCount = f.OtherWave,
                ArbitrateCount = f.Arbitrate,
                RemainingCount = f.Remaining,
                RouteCount = f.RouteCount,
                VolumeApplies = applies,
                VolumeBytes = applies ? Volume(f.Type, volumes.GetValueOrDefault(f.ObjectId)) : null,
                UnresolvedLinks = f.Unresolved,
                MembersNotCollected = f.MembersNotCollected,
                AllUsers = f.AllUsers,
                Decision = decision?.Value,
                DecisionWave = decision?.Wave
            };
        }).ToList();
        var shared = resources.Where(r => r.Classification == DependencyClassification.Shared).ToList();
        var analysis = new DependencyAnalysis
        {
            Id = analysisId,
            WorkspaceId = ws,
            ProjectId = project.Id,
            EnvironmentId = env,
            Wave = label,
            OperationId = operationId,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            ComputedAtUtc = DateTimeOffset.UtcNow,
            MigratingPrincipals = result.Migrating,
            OtherWavePrincipals = result.OtherWave,
            ArbitratePrincipals = result.Arbitrate,
            RemainingPrincipals = result.Remaining,
            Resources = resources.Count,
            SharedResources = shared.Count,
            UnresolvedLinks = resources.Sum(r => r.UnresolvedLinks),
            KnownVolumeBytes = resources.Sum(r => r.VolumeBytes ?? 0),
            SharedVolumeBytes = shared.Sum(r => r.VolumeBytes ?? 0),
            UnknownVolumeResources = resources.Count(r => r.VolumeApplies && r.VolumeBytes is null),
            CoverageJson = JsonSerializer.Serialize(coverage, ContractJson.Options)
        };
        var previous = db.DependencyAnalyses.Where(a => a.WorkspaceId == ws && a.ProjectId == project.Id && a.EnvironmentId == env && a.Wave == label);
        await db.DependencyLinks.Where(l => l.WorkspaceId == ws && previous.Any(a => a.Id == l.AnalysisId)).ExecuteDeleteAsync(token);
        await db.DependencyResources.Where(r => r.WorkspaceId == ws && previous.Any(a => a.Id == r.AnalysisId)).ExecuteDeleteAsync(token);
        await previous.ExecuteDeleteAsync(token);

        db.DependencyAnalyses.Add(analysis);
        db.DependencyResources.AddRange(resources);
        foreach (var finding in result.Findings)
        {
            db.DependencyLinks.AddRange(finding.Routes.Select(r => new DependencyLink
            {
                WorkspaceId = ws,
                AnalysisId = analysis.Id,
                ResourceObjectId = finding.ObjectId,
                Kind = DependencyLinkKind.Route,
                RelatedObjectId = r.ObjectId,
                Route = r.Relation,
                RelationshipId = r.RelationshipId
            }));
            db.DependencyLinks.AddRange(finding.Samples.Select(s => new DependencyLink
            {
                WorkspaceId = ws,
                AnalysisId = analysis.Id,
                ResourceObjectId = finding.ObjectId,
                Kind = DependencyLinkKind.Principal,
                RelatedObjectId = s.UserId,
                Side = s.Side,
                Route = s.Route,
                ViaObjectId = s.Via
            }));
        }
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = ws,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            Action = Analyzed,
            ResourceId = operationId,
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return analysis.Id;
    }

    public async Task<IReadOnlyList<DependencySummary>> AnalysesAsync(Guid projectId, EnvironmentRole side, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var rows = await (from a in db.DependencyAnalyses.AsNoTracking()
                          join e in db.Environments on new { a.WorkspaceId, Id = a.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                          where a.WorkspaceId == project.WorkspaceId && a.ProjectId == project.Id && e.Role == side
                          orderby a.ComputedAtUtc descending, a.Id
                          select new { a, e.ExternalId }).Take(50).ToListAsync(token);
        var ids = rows.Select(r => r.a.Id).ToList();
        var families = (await db.DependencyResources.AsNoTracking().Where(r => r.WorkspaceId == project.WorkspaceId && ids.Contains(r.AnalysisId))
            .GroupBy(r => new { r.AnalysisId, r.Family })
            .Select(g => new { g.Key.AnalysisId, g.Key.Family, Count = g.Count(), Shared = g.Count(r => r.Classification == DependencyClassification.Shared) })
            .ToListAsync(token)).ToLookup(x => x.AnalysisId);
        return rows.Select(r => new DependencySummary(r.a.Id, r.a.EnvironmentId, r.ExternalId, r.a.Wave, r.a.ComputedAtUtc, r.a.ActorObjectId,
            r.a.MigratingPrincipals, r.a.OtherWavePrincipals, r.a.ArbitratePrincipals, r.a.RemainingPrincipals, r.a.Resources, r.a.SharedResources,
            r.a.UnresolvedLinks, r.a.KnownVolumeBytes, r.a.SharedVolumeBytes, r.a.UnknownVolumeResources,
            JsonSerializer.Deserialize<List<DependencyFamilyCoverage>>(r.a.CoverageJson, ContractJson.Options)!,
            families[r.a.Id].OrderBy(f => f.Family).Select(f => new DependencyFamilyCount(f.Family, f.Count, f.Shared)).ToList())).ToList();
    }

    public async Task<PageResult<DependencyResourceRow>> ResourcesAsync(Guid projectId, Guid analysisId, DependencyFilter filter, PageRequest page,
        CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        await RequireAnalysisAsync(db, project, analysisId, token);
        var term = page.Search.Trim();
        var query = db.DependencyResources.AsNoTracking().Where(r => r.WorkspaceId == project.WorkspaceId && r.AnalysisId == analysisId
            && (filter.Family == null || r.Family == filter.Family) && (filter.Classification == null || r.Classification == filter.Classification)
            && (term == "" || r.DisplayName!.Contains(term)));
        var rows = await query
            .OrderBy(r => r.Classification == DependencyClassification.Shared ? 0 : r.Classification == DependencyClassification.ToArbitrate ? 1
                : r.Classification == DependencyClassification.CrossWave ? 2 : 3)
            .ThenBy(r => r.Family).ThenBy(r => r.DisplayName).ThenBy(r => r.ObjectId).Skip(page.Start).Take(page.Count)
            .Select(r => new DependencyResourceRow(r.ObjectId, r.Family, r.Type, r.DisplayName, r.Classification, r.MigratingCount, r.OtherWaveCount,
                r.ArbitrateCount, r.RemainingCount, r.RouteCount, r.VolumeApplies, r.VolumeBytes, r.UnresolvedLinks, r.MembersNotCollected, r.AllUsers,
                r.Decision, r.DecisionWave)).ToListAsync(token);
        return new(rows, await query.CountAsync(token));
    }

    public async Task<IReadOnlyList<DependencyLinkRow>> LinksAsync(Guid projectId, Guid analysisId, Guid objectId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        await RequireAnalysisAsync(db, project, analysisId, token);
        var rows = await (from l in db.DependencyLinks.AsNoTracking()
                          join i in db.InventoryObjects on new { l.WorkspaceId, Id = l.RelatedObjectId } equals new { i.WorkspaceId, i.Id }
                          where l.WorkspaceId == project.WorkspaceId && l.AnalysisId == analysisId && l.ResourceObjectId == objectId
                          select new
                          {
                              l.Kind,
                              l.RelatedObjectId,
                              Label = i.DisplayName ?? i.UserPrincipalName,
                              i.Type,
                              l.Side,
                              l.Route,
                              l.ViaObjectId,
                              Via = db.InventoryObjects.Where(v => v.WorkspaceId == l.WorkspaceId && v.Id == l.ViaObjectId)
                                  .Select(v => v.DisplayName ?? v.UserPrincipalName).FirstOrDefault(),
                              l.RelationshipId
                          }).ToListAsync(token);
        return rows.OrderBy(r => r.Kind == DependencyLinkKind.Route ? 0 : 1).ThenBy(r => r.Side).ThenBy(r => r.Label, StringComparer.CurrentCulture)
            .ThenBy(r => r.RelatedObjectId)
            .Select(r => new DependencyLinkRow(r.Kind, r.RelatedObjectId, r.Label, r.Type, r.Side, r.Route, r.ViaObjectId, r.Via, r.RelationshipId)).ToList();
    }

    private static async Task RequireAnalysisAsync(ClarityDbContext db, Project project, Guid analysisId, CancellationToken token)
    {
        if (!await db.DependencyAnalyses.AnyAsync(a => a.WorkspaceId == project.WorkspaceId && a.ProjectId == project.Id && a.Id == analysisId, token))
        { throw new AccessDeniedException(); }
    }

    private static DependencySide Side(ScopeValue value, string? wave, string? selected) => value switch
    {
        ScopeValue.Include => selected is null || wave == selected ? DependencySide.Migrating : DependencySide.OtherWave,
        ScopeValue.ToArbitrate => DependencySide.ToArbitrate,
        _ => DependencySide.Remaining
    };

    /// <summary>
    /// A principal's side comes from its own decision, else from decisions on objects bound to it by a validated hybrid mapping;
    /// conflicting mapped decisions stay to arbitrate.
    /// </summary>
    private static async Task<Dictionary<Guid, DependencySide>> SidesAsync(ClarityDbContext db, Project project, Guid env, string? label,
        IReadOnlyList<DependencyNode> nodes, Dictionary<Guid, DependencySide> own, CancellationToken token)
    {
        var users = nodes.Where(n => n.Type == "user").Select(n => n.Id).ToHashSet();
        var sides = own.Where(x => users.Contains(x.Key)).ToDictionary();
        var links = await db.MappingDecisions.AsNoTracking().Where(m => m.WorkspaceId == project.WorkspaceId && m.ProjectId == project.Id
                && m.State == MappingState.Validated && (m.EntraEnvironmentId == env || m.AdEnvironmentId == env))
            .Select(m => new { Local = m.EntraEnvironmentId == env ? m.EntraObjectId : m.AdObjectId, Remote = m.EntraEnvironmentId == env ? m.AdObjectId : m.EntraObjectId })
            .ToListAsync(token);
        links = links.Where(l => users.Contains(l.Local) && !sides.ContainsKey(l.Local)).ToList();
        var remote = new Dictionary<Guid, DependencySide>();
        foreach (var chunk in links.Select(l => l.Remote).Distinct().Chunk(500))
        {
            foreach (var d in await db.ScopeDecisions.AsNoTracking().Where(d => d.WorkspaceId == project.WorkspaceId && d.ProjectId == project.Id
                && chunk.Contains(d.ObjectId)).Select(d => new { d.ObjectId, d.Value, d.Wave }).ToListAsync(token))
            { remote[d.ObjectId] = Side(d.Value, d.Wave, label); }
        }
        foreach (var group in links.Where(l => remote.ContainsKey(l.Remote)).GroupBy(l => l.Local))
        {
            var found = group.Select(l => remote[l.Remote]).Distinct().ToList();
            sides[group.Key] = found.Count == 1 ? found[0] : DependencySide.ToArbitrate;
        }
        return sides;
    }

    private static bool Tenantwide(CatalogSection section) => section.SupportedScopes.Any(s => s is "tenant" or "domain");

    /// <summary>Known storage in bytes; null when the producing section was not published or the value is not a usable number.</summary>
    internal static long? Volume(string type, IReadOnlyDictionary<string, string>? fields)
    {
        if (fields is null) { return null; }
        long? Read(string field)
        {
            if (!fields.TryGetValue(field, out var json)) { return null; }
            var value = JsonSerializer.Deserialize<JsonElement>(json);
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0 ? number
                : value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var real) && real >= 0 && real < long.MaxValue ? (long)real
                : null;
        }
        return type switch
        {
            "site" => Read("storageUsedBytes"),
            "oneDrive" => Read("quotaUsedBytes") ?? Read("storageUsedBytes") ?? (Read("storageUsedMegabytes") is { } mb && mb < long.MaxValue / 1048576 ? mb * 1048576 : null),
            _ => Read("sizeBytes") is { } size ? size + (Read("archiveSizeBytes") ?? 0) : Read("storageUsedBytes")
        };
    }
}
