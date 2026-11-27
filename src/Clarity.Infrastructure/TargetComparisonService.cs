using System.Security.Cryptography;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record TargetRuleDraft(string ObjectType, TargetRuleKind Kind, string SourceField, string TargetField, string? TargetDomain,
    int Priority, string Justification);
/// <summary>Coverage of one object type on one side; MissingFields lists the rule fields no published section of that type collected.</summary>
public sealed record TargetCoverageRow(EnvironmentRole Side, string Type, int Objects, bool Exhaustive, IReadOnlyList<string> MissingFields);
public sealed record TargetComparisonSummary(Guid Id, Guid SourceEnvironmentId, string Source, Guid TargetEnvironmentId, string Target,
    TargetState TargetState, string? Hypotheses, TargetDataState Data, DateTimeOffset ComputedAtUtc, Guid ActorObjectId, int Matched,
    int Ambiguous, int Absent, int Unknown, int NotCompared, int Collisions, IReadOnlyList<TargetCoverageRow> Coverage,
    IReadOnlyList<TargetRuleInput> Rules);
public sealed record TargetItemRow(Guid Id, string ObjectType, TargetOutcome? Outcome, CollisionKind? Collision, Guid? SourceId, string? Label,
    Guid? TargetId, string? TargetLabel, Guid? OtherId, string? OtherLabel, string Key, int CandidateCount, string Reason, Guid? RuleId);

/// <summary>
/// Documents a project's source-to-target mapping rules and compares a source environment with a planned or existing target. Data
/// comparisons run only when the target exists and has published data; each comparison replaces the previous snapshot of the same pair and
/// proposes source-target associations that the consultant reviews like hybrid ones.
/// </summary>
public sealed class TargetComparisonService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope,
    DiscoveryCatalog catalog)
{
    private const string RuleAdded = "TargetMapping.RuleAdded";
    private const string RuleRetired = "TargetMapping.RuleRetired";
    private const string Computed = "TargetComparison.Computed";
    private static readonly string[] BaseFields = ["userPrincipalName", "mail", "proxyAddresses", "alias", "isVerified", "displayName", "externalDirectoryObjectId"];

    public async Task<IReadOnlyList<TargetMappingRule>> RulesAsync(Guid projectId, bool includeRetired, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.TargetMappingRules.AsNoTracking().Where(r => r.WorkspaceId == project.WorkspaceId && r.ProjectId == project.Id
                && (includeRetired || r.RetiredAtUtc == null))
            .OrderBy(r => r.RetiredAtUtc != null).ThenBy(r => r.ObjectType).ThenBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync(token);
    }

    public async Task<Guid> AddRuleAsync(Guid projectId, TargetRuleDraft draft, Guid operationId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        if (operationId == Guid.Empty) { throw new ArgumentException("An operation identifier is required."); }
        var rule = new TargetMappingRule
        {
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            ObjectType = draft.ObjectType.Trim(),
            Kind = draft.Kind,
            SourceField = draft.SourceField.Trim(),
            TargetField = draft.Kind == TargetRuleKind.ComputedUpn ? "userPrincipalName" : draft.TargetField.Trim(),
            TargetDomain = draft.Kind == TargetRuleKind.ComputedUpn ? draft.TargetDomain?.Trim().ToLowerInvariant() : null,
            Priority = draft.Priority,
            Justification = draft.Justification.Trim(),
            OperationId = operationId,
            CreatedByTenantId = scope.Identity.TenantId,
            CreatedByObjectId = scope.Identity.ObjectId
        };
        TargetComparisonRules.Validate(rule);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var ws = project.WorkspaceId;
        var replay = await db.TargetMappingRules.AsNoTracking().SingleOrDefaultAsync(r => r.WorkspaceId == ws && r.OperationId == operationId, token);
        if (replay is not null)
        {
            if (replay.ProjectId != rule.ProjectId || replay.ObjectType != rule.ObjectType || replay.Kind != rule.Kind
                || replay.SourceField != rule.SourceField || replay.TargetField != rule.TargetField || replay.TargetDomain != rule.TargetDomain
                || replay.Priority != rule.Priority || replay.Justification != rule.Justification
                || replay.CreatedByTenantId != rule.CreatedByTenantId || replay.CreatedByObjectId != rule.CreatedByObjectId)
            { throw new ConflictException(); }
            return replay.Id;
        }
        if (await db.AuditEvents.AnyAsync(a => a.WorkspaceId == ws && a.ResourceId == operationId, token)) { throw new ConflictException(); }
        if (await db.TargetMappingRules.CountAsync(r => r.WorkspaceId == ws && r.ProjectId == project.Id && r.RetiredAtUtc == null, token)
            >= TargetComparisonRules.MaxRules)
        { throw new ArgumentException("Retire a rule before adding another."); }
        db.TargetMappingRules.Add(rule);
        Audit(db, ws, RuleAdded, operationId);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return rule.Id;
    }

    /// <summary>Retires a rule with a justification; associations it proposed are flagged for review at the next comparison.</summary>
    public async Task RetireRuleAsync(Guid projectId, Guid ruleId, string reason, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        InventoryMatching.ValidateReason(reason);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var rule = await db.TargetMappingRules.SingleOrDefaultAsync(r => r.WorkspaceId == project.WorkspaceId && r.ProjectId == project.Id
            && r.Id == ruleId, token) ?? throw new AccessDeniedException();
        if (rule.RetiredAtUtc is not null) { return; }
        rule.RetiredAtUtc = DateTimeOffset.UtcNow;
        rule.RetiredByTenantId = scope.Identity.TenantId;
        rule.RetiredByObjectId = scope.Identity.ObjectId;
        rule.RetirementReason = reason.Trim();
        Audit(db, project.WorkspaceId, RuleRetired, rule.Id);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task<Guid> CompareAsync(Guid projectId, Guid sourceEnvironmentId, Guid targetEnvironmentId, Guid operationId,
        CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        if (operationId == Guid.Empty) { throw new ArgumentException("An operation identifier is required."); }
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var ws = project.WorkspaceId;
        var replay = await db.TargetComparisons.AsNoTracking().SingleOrDefaultAsync(c => c.WorkspaceId == ws && c.OperationId == operationId, token);
        if (replay is not null)
        {
            if (replay.ProjectId != project.Id || replay.SourceEnvironmentId != sourceEnvironmentId || replay.TargetEnvironmentId != targetEnvironmentId
                || replay.ActorTenantId != scope.Identity.TenantId || replay.ActorObjectId != scope.Identity.ObjectId) { throw new ConflictException(); }
            return replay.Id;
        }
        if (await db.AuditEvents.AnyAsync(a => a.WorkspaceId == ws && a.ResourceId == operationId, token)) { throw new ConflictException(); }
        var environments = await db.Environments.AsNoTracking().Where(e => e.WorkspaceId == ws && e.ProjectId == project.Id
            && (e.Id == sourceEnvironmentId || e.Id == targetEnvironmentId)).ToDictionaryAsync(e => e.Id, token);
        if (!environments.TryGetValue(sourceEnvironmentId, out var sourceEnv) || !environments.TryGetValue(targetEnvironmentId, out var targetEnv))
        { throw new AccessDeniedException(); }
        if (sourceEnv.Role != EnvironmentRole.Source || targetEnv.Role != EnvironmentRole.Target)
        { throw new ArgumentException("Compare a source environment with a target environment."); }

        var rules = await db.TargetMappingRules.AsNoTracking().Where(r => r.WorkspaceId == ws && r.ProjectId == project.Id && r.RetiredAtUtc == null)
            .OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync(token);
        var inputs = rules.Select(r => new TargetRuleInput(r.Id, r.ObjectType, r.Kind, r.SourceField, r.TargetField, r.TargetDomain, r.Priority)).ToList();
        var ruleTypes = inputs.Select(r => r.ObjectType).Distinct(StringComparer.Ordinal).ToList();
        var types = ruleTypes.Union(TargetComparisonRules.AddressTypes, StringComparer.Ordinal).ToList();
        var fields = BaseFields.Union(inputs.SelectMany(r => new[] { r.SourceField, r.TargetField }), StringComparer.Ordinal).ToList();

        var source = await LoadAsync(db, ws, sourceEnv, types, fields, token);
        var targetData = targetEnv.TargetState == TargetState.Planned ? TargetDataState.Planned : TargetDataState.NoData;
        var target = new Side(new([], new Dictionary<string, TargetCoverage>(), [], false), new Dictionary<(Guid, string), Guid>(), 0);
        if (targetData == TargetDataState.NoData)
        {
            target = await LoadAsync(db, ws, targetEnv, types, fields, token);
            if (target.Sections != 0) { targetData = TargetDataState.Available; }
        }
        var result = TargetComparisonEngine.Compare(inputs, source.Data, target.Data, targetData);

        var labels = source.Data.Objects.Concat(target.Data.Objects).GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First().Label);
        foreach (var domain in source.Data.Domains.Concat(target.Data.Domains)) { labels.TryAdd(domain.ObjectId, domain.Name); }
        var coverage = Coverage(EnvironmentRole.Source, source.Data, inputs, r => r.SourceField)
            .Concat(targetData == TargetDataState.Available ? Coverage(EnvironmentRole.Target, target.Data, inputs, r => r.TargetField) : []).ToList();
        var comparison = new TargetComparison
        {
            WorkspaceId = ws,
            ProjectId = project.Id,
            SourceEnvironmentId = sourceEnv.Id,
            TargetEnvironmentId = targetEnv.Id,
            Data = targetData,
            OperationId = operationId,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            ComputedAtUtc = DateTimeOffset.UtcNow,
            Matched = result.Count(TargetOutcome.Matched),
            Ambiguous = result.Count(TargetOutcome.Ambiguous),
            Absent = result.Count(TargetOutcome.Absent),
            Unknown = result.Count(TargetOutcome.Unknown),
            NotCompared = result.Count(TargetOutcome.NotCompared),
            Collisions = result.Collisions.Count,
            RulesJson = JsonSerializer.Serialize(inputs, ContractJson.Options),
            CoverageJson = JsonSerializer.Serialize(coverage, ContractJson.Options)
        };
        var previous = db.TargetComparisons.Where(c => c.WorkspaceId == ws && c.ProjectId == project.Id && c.SourceEnvironmentId == sourceEnv.Id
            && c.TargetEnvironmentId == targetEnv.Id);
        await db.TargetComparisonItems.Where(i => i.WorkspaceId == ws && previous.Any(c => c.Id == i.ComparisonId)).ExecuteDeleteAsync(token);
        await previous.ExecuteDeleteAsync(token);
        db.TargetComparisons.Add(comparison);
        string? Label(Guid? id) => id is { } value && labels.TryGetValue(value, out var label) ? Cut(label, 400) : null;
        db.TargetComparisonItems.AddRange(result.Outcomes.Select(o => new TargetComparisonItem
        {
            WorkspaceId = ws,
            ComparisonId = comparison.Id,
            SourceEnvironmentId = sourceEnv.Id,
            TargetEnvironmentId = targetEnv.Id,
            SourceObjectId = o.SourceId,
            TargetObjectId = o.Matches.Count == 1 ? o.Matches[0].TargetId : null,
            OtherObjectId = o.Sharers.Count == 0 ? null : o.Sharers[0],
            Outcome = o.Outcome,
            ObjectType = o.Type,
            Label = Label(o.SourceId),
            TargetLabel = o.Matches.Count == 1 ? Label(o.Matches[0].TargetId) : null,
            OtherLabel = o.Sharers.Count == 0 ? null : Label(o.Sharers[0]),
            Key = o.Key,
            CandidateCount = o.Matches.Count,
            Reason = o.Reason,
            RuleId = o.RuleId
        }));
        db.TargetComparisonItems.AddRange(result.Collisions.Select(c => new TargetComparisonItem
        {
            WorkspaceId = ws,
            ComparisonId = comparison.Id,
            SourceEnvironmentId = sourceEnv.Id,
            TargetEnvironmentId = targetEnv.Id,
            SourceObjectId = c.SourceId,
            TargetObjectId = c.TargetId,
            OtherObjectId = c.OtherSourceId,
            Collision = c.Kind,
            ObjectType = c.Type,
            Label = Label(c.SourceId),
            TargetLabel = Label(c.TargetId),
            OtherLabel = Label(c.OtherSourceId),
            Key = c.Key,
            Reason = c.Reason,
            RuleId = c.RuleId
        }));
        if (targetData == TargetDataState.Available) { await ProposeAsync(db, project, sourceEnv.Id, targetEnv.Id, rules, result, source, target, token); }
        Audit(db, ws, Computed, operationId);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return comparison.Id;
    }

    public async Task<IReadOnlyList<TargetComparisonSummary>> ComparisonsAsync(Guid projectId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var rows = await (from c in db.TargetComparisons.AsNoTracking()
                          join s in db.Environments on new { c.WorkspaceId, Id = c.SourceEnvironmentId } equals new { s.WorkspaceId, s.Id }
                          join t in db.Environments on new { c.WorkspaceId, Id = c.TargetEnvironmentId } equals new { t.WorkspaceId, t.Id }
                          where c.WorkspaceId == project.WorkspaceId && c.ProjectId == project.Id
                          orderby c.ComputedAtUtc descending, c.Id
                          select new { c, Source = s.ExternalId, Target = t.ExternalId, t.TargetState, t.Hypotheses }).Take(50).ToListAsync(token);
        return rows.Select(r => new TargetComparisonSummary(r.c.Id, r.c.SourceEnvironmentId, r.Source, r.c.TargetEnvironmentId, r.Target,
            r.TargetState, r.Hypotheses, r.c.Data, r.c.ComputedAtUtc, r.c.ActorObjectId, r.c.Matched, r.c.Ambiguous, r.c.Absent, r.c.Unknown,
            r.c.NotCompared, r.c.Collisions, JsonSerializer.Deserialize<List<TargetCoverageRow>>(r.c.CoverageJson, ContractJson.Options)!,
            JsonSerializer.Deserialize<List<TargetRuleInput>>(r.c.RulesJson, ContractJson.Options)!)).ToList();
    }

    /// <summary>One page of per-object outcomes (collisions false) or of collisions (collisions true), filtered and paged in the database.</summary>
    public async Task<PageResult<TargetItemRow>> ItemsAsync(Guid projectId, Guid comparisonId, bool collisions, TargetOutcome? outcome,
        CollisionKind? collision, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var comparison = await db.TargetComparisons.AsNoTracking().SingleOrDefaultAsync(c => c.WorkspaceId == project.WorkspaceId
            && c.ProjectId == project.Id && c.Id == comparisonId, token) ?? throw new AccessDeniedException();
        var term = page.Search.Trim();
        var query = db.TargetComparisonItems.AsNoTracking().Where(i => i.WorkspaceId == project.WorkspaceId && i.ComparisonId == comparison.Id
            && (collisions ? i.Collision != null && (collision == null || i.Collision == collision)
                : i.Outcome != null && (outcome == null || i.Outcome == outcome))
            && (term == "" || i.Label!.Contains(term) || i.TargetLabel!.Contains(term) || i.Key.Contains(term)));
        var ordered = collisions
            ? query.OrderBy(i => i.Collision).ThenBy(i => i.Key).ThenBy(i => i.Label).ThenBy(i => i.Id)
            : query.OrderBy(i => i.Outcome).ThenBy(i => i.Label).ThenBy(i => i.Id);
        var rows = await ordered.Skip(page.Start).Take(page.Count).Select(i => new TargetItemRow(i.Id, i.ObjectType, i.Outcome, i.Collision,
            i.SourceObjectId, i.Label, i.TargetObjectId, i.TargetLabel, i.OtherObjectId, i.OtherLabel, i.Key, i.CandidateCount, i.Reason, i.RuleId))
            .ToListAsync(token);
        return new(rows, await query.CountAsync(token));
    }

    private sealed record Side(TargetSideData Data, IReadOnlyDictionary<(Guid, string), Guid> Observations, int Sections);

    private static IEnumerable<TargetCoverageRow> Coverage(EnvironmentRole role, TargetSideData data, IReadOnlyList<TargetRuleInput> rules,
        Func<TargetRuleInput, string> field) =>
        rules.GroupBy(r => r.ObjectType, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var coverage = data.Coverage.GetValueOrDefault(g.Key);
            return new TargetCoverageRow(role, g.Key, data.Objects.Count(o => o.Type == g.Key), coverage?.Exhaustive == true,
                g.Select(field).Distinct(StringComparer.Ordinal).Where(f => coverage?.Fields.Contains(f) != true).Order(StringComparer.Ordinal).ToList());
        });

    /// <summary>
    /// Loads observed objects with their current values. Coverage uses the latest published section of each capability and scope: a type is
    /// exhaustive only when such a section is complete, tenant-wide and exhaustive by catalog, so absence is never inferred from partial data.
    /// </summary>
    private async Task<Side> LoadAsync(ClarityDbContext db, Guid ws, ProjectEnvironment environment, IReadOnlyList<string> types,
        IReadOnlyList<string> fields, CancellationToken token)
    {
        var env = environment.Id;
        var latest = (await ProjectedSection.LoadAsync(db, ws, env, token))
            .GroupBy(s => (s.Section.CapabilityId, s.Section.ScopeHash))
            .Select(g => g.OrderByDescending(s => s.Completed).ThenByDescending(s => s.ExternalRunId).First()).ToList();
        var sectionIds = latest.Select(s => s.Section.Id).ToList();
        var typed = await (from o in db.ObjectObservations.AsNoTracking()
                           where o.WorkspaceId == ws && sectionIds.Contains(o.SectionRunId)
                           join i in db.InventoryObjects on new { o.WorkspaceId, Id = o.ObjectId } equals new { i.WorkspaceId, i.Id }
                           select new { o.SectionRunId, i.Type }).Distinct().ToListAsync(token);
        var bySection = latest.ToDictionary(s => s.Section.Id);
        var coverage = typed.GroupBy(t => t.Type, StringComparer.Ordinal).ToDictionary(g => g.Key, g =>
        {
            var sections = g.Select(t => bySection[t.SectionRunId]).ToList();
            var covered = sections.SelectMany(s => JsonSerializer.Deserialize<SectionResult>(s.Section.ResultJson, ContractJson.Options)!.CoveredFields)
                .ToHashSet(StringComparer.Ordinal);
            var exhaustive = sections.Any(s => s.IsExhaustive(catalog)
                && JsonSerializer.Deserialize<CollectionScope>(s.Section.ScopeJson, ContractJson.Options)?.Kind == "tenant");
            return new TargetCoverage(covered, exhaustive);
        }, StringComparer.Ordinal);

        var objects = db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == ws && i.EnvironmentId == env
            && i.Presence == InventoryPresence.Observed && types.Contains(i.Type));
        if (await objects.CountAsync(token) > TargetComparisonRules.MaxObjects)
        { throw new ArgumentException("The environments are too large for one comparison."); }
        var items = await objects.Select(i => new { i.Id, i.Type, Label = i.UserPrincipalName ?? i.DisplayName ?? i.Smtp }).ToListAsync(token);
        var values = await (from v in db.CurrentValues.AsNoTracking()
                            where v.WorkspaceId == ws && fields.Contains(v.Field)
                            join i in objects on new { v.WorkspaceId, Id = v.ObjectId } equals new { i.WorkspaceId, i.Id }
                            select new { v.ObjectId, v.Field, v.ValueJson, v.Conflicting, v.ObservationId }).ToListAsync(token);
        var observations = values.ToDictionary(v => (v.ObjectId, v.Field), v => v.ObservationId);
        var byObject = values.ToLookup(v => v.ObjectId);

        var owners = await db.InventoryRelationships.AsNoTracking().Where(r => r.WorkspaceId == ws && r.EnvironmentId == env && r.IsCurrent
                && r.RelationType == "mailboxOf" && r.FromObjectId != null && r.ToObjectId != null)
            .Select(r => new { From = r.FromObjectId!.Value, To = r.ToObjectId!.Value }).ToListAsync(token);
        var owner = owners.GroupBy(o => o.From).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().To);
        // Exchange objects carry the Entra object ID of their directory object; it links groups and shared mailboxes to that principal.
        var directory = await db.ExternalKeys.AsNoTracking().Where(k => k.WorkspaceId == ws && k.EnvironmentId == env && k.IsPrimary
                && k.Namespace == "microsoft.graph" && (k.Type == "user" || k.Type == "group"))
            .Select(k => new { k.ExternalId, k.ObjectId }).ToListAsync(token);
        var byDirectoryId = directory.GroupBy(k => k.ExternalId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().ObjectId, StringComparer.OrdinalIgnoreCase);

        var candidates = items.Select(i =>
        {
            var current = byObject[i.Id].ToList();
            var parsed = current.ToDictionary(v => v.Field, v => Parse(v.ValueJson), StringComparer.Ordinal);
            Guid? principal = owner.TryGetValue(i.Id, out var o) ? o
                : parsed.GetValueOrDefault("externalDirectoryObjectId") is [var edo] && byDirectoryId.TryGetValue(edo, out var d) && d != i.Id ? d : null;
            return new TargetCandidate(i.Id, i.Type, i.Label ?? i.Id.ToString(), parsed,
                current.Where(v => v.Conflicting).Select(v => v.Field).ToHashSet(StringComparer.Ordinal), principal);
        }).ToList();

        var domainRows = await db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == ws && i.EnvironmentId == env
                && i.Presence == InventoryPresence.Observed && TargetComparisonRules.DomainTypes.Contains(i.Type))
            .Select(i => new
            {
                i.Id,
                i.Type,
                i.DisplayName,
                Key = db.ExternalKeys.Where(k => k.WorkspaceId == ws && k.ObjectId == i.Id && k.IsPrimary).Select(k => k.ExternalId).FirstOrDefault(),
                Verified = db.CurrentValues.Where(v => v.WorkspaceId == ws && v.ObjectId == i.Id && v.Field == "isVerified").Select(v => v.ValueJson)
                    .FirstOrDefault()
            }).ToListAsync(token);
        var domains = domainRows.Select(d => new ObservedDomain(d.Id, (d.Type == "domain" ? d.Key : d.DisplayName) ?? "",
                d.Type == "acceptedDomain" || d.Verified == "true"))
            .Where(d => TargetComparisonRules.IsDomain(d.Name.ToLowerInvariant())).ToList();
        var hasDomains = latest.Any(s => s.Section.CapabilityId is "entra.domains" or "exchange.acceptedDomains");
        return new Side(new(candidates, coverage, domains, hasDomains), observations, latest.Count);
    }

    private static IReadOnlyList<string> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return root.ValueKind switch
        {
            JsonValueKind.String => [root.GetString()!],
            JsonValueKind.Array => root.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText()).ToList(),
            JsonValueKind.True => ["true"],
            JsonValueKind.False => ["false"],
            JsonValueKind.Number => [root.GetRawText()],
            _ => []
        };
    }

    /// <summary>
    /// Proposes one source-target association per candidate pair: a single candidate is proposed, an ambiguous one goes to review. Unchanged
    /// evidence keeps the consultant's decision; changed evidence or a lost candidate sends only that association back to review.
    /// </summary>
    private async Task ProposeAsync(ClarityDbContext db, Project project, Guid sourceEnv, Guid targetEnv, IReadOnlyList<TargetMappingRule> rules,
        TargetComparisonResult result, Side source, Side target, CancellationToken token)
    {
        var ws = project.WorkspaceId;
        var kinds = rules.ToDictionary(r => r.Id);
        var existing = await db.MappingDecisions.Where(m => m.WorkspaceId == ws && m.ProjectId == project.Id && m.Kind == MappingKind.SourceTarget
            && m.AdEnvironmentId == sourceEnv && m.EntraEnvironmentId == targetEnv).ToDictionaryAsync(m => (m.AdObjectId, m.EntraObjectId), token);
        foreach (var outcome in result.Outcomes.Where(o => o.Outcome is TargetOutcome.Matched or TargetOutcome.Ambiguous))
        {
            var candidates = outcome.Matches.Select(m => m.TargetId).Order().ToArray();
            foreach (var match in outcome.Matches)
            {
                var rule = kinds[match.RuleId];
                var associationRule = rule.Kind == TargetRuleKind.ComputedUpn ? AssociationRule.ComputedTargetUpn : AssociationRule.MappingRuleAttribute;
                var proof = new[] { source.Observations.GetValueOrDefault((outcome.SourceId, rule.SourceField)),
                    target.Observations.GetValueOrDefault((match.TargetId, rule.TargetField)) }.Where(id => id != Guid.Empty).Distinct().Order();
                var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    rule = match.RuleId,
                    key = match.Key.ToLowerInvariant(),
                    candidates,
                    sharers = outcome.Sharers
                })));
                var evidence = JsonSerializer.Serialize(new
                {
                    rule = associationRule.ToString(),
                    mappingRule = match.RuleId,
                    match.Key,
                    candidates,
                    sharers = outcome.Sharers,
                    observations = proof
                });
                if (!existing.Remove((outcome.SourceId, match.TargetId), out var decision))
                {
                    decision = new MappingDecision
                    {
                        WorkspaceId = ws,
                        ProjectId = project.Id,
                        Kind = MappingKind.SourceTarget,
                        AdEnvironmentId = sourceEnv,
                        EntraEnvironmentId = targetEnv,
                        AdObjectId = outcome.SourceId,
                        EntraObjectId = match.TargetId,
                        Fingerprint = fingerprint,
                        EvidenceJson = evidence,
                        Rule = associationRule
                    };
                    db.MappingDecisions.Add(decision);
                    InventoryMatching.Record(db, decision, outcome.Outcome == TargetOutcome.Matched ? MappingState.Proposed : MappingState.ToReview,
                        "CandidateObserved", null);
                }
                else if (decision.Fingerprint != fingerprint)
                {
                    decision.Rule = associationRule;
                    decision.Fingerprint = fingerprint;
                    decision.EvidenceJson = evidence;
                    InventoryMatching.Record(db, decision, MappingState.ToReview, "AssociationEvidenceChanged", null);
                }
            }
        }
        foreach (var lost in existing.Values.Where(m => m.Rule != AssociationRule.NoCurrentCandidate))
        {
            lost.Fingerprint = "";
            lost.Rule = AssociationRule.NoCurrentCandidate;
            InventoryMatching.Record(db, lost, MappingState.ToReview, "CandidateNoLongerObserved", null);
        }
    }

    private void Audit(ClarityDbContext db, Guid ws, string action, Guid resource) => db.AuditEvents.Add(new AuditEvent
    {
        WorkspaceId = ws,
        ActorTenantId = scope.Identity.TenantId,
        ActorObjectId = scope.Identity.ObjectId,
        Action = action,
        ResourceId = resource,
        CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
    });

    private static string Cut(string value, int length) => value.Length > length ? value[..(length - 1)] + "…" : value;
}
