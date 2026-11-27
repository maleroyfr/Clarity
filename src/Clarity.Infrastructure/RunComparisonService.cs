using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record ComparableRun(Guid Id, Guid ExternalRunId, string Producer, string Version, CollectionChannel Channel,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, int Sections);
public sealed record ComparisonSummary(Guid Id, Guid EnvironmentId, string Environment, Guid BaselineRunId, DateTimeOffset BaselineStartedAtUtc,
    Guid LaterRunId, DateTimeOffset LaterStartedAtUtc, DateTimeOffset ComputedAtUtc, Guid ActorObjectId, int Added, int Modified, int Removed,
    int NewlyObserved, int NotObserved, int NotCompared, int Unchanged, int RelationsAdded, int RelationsRemoved, int RelationsUnknown,
    IReadOnlyList<ComparisonUnit> Units);
public sealed record ComparisonItemRow(Guid ObjectId, string Type, string? DisplayName, RunChange Change, bool Tombstone, string ChangedFields,
    IReadOnlyList<FieldChange> Changes, bool ScopeReview, bool MappingReview, bool AssessmentReview, bool HasAffectedDecision);
/// <summary>A live consultant decision on a changed object; Label is the resource key of its current value.</summary>
public sealed record ComparisonDecisionRow(DecisionKind Kind, Guid ObjectId, string? DisplayName, string Type, RunChange Change, string ChangedFields,
    string Label, string? Detail);

/// <summary>
/// Compares two collections of one environment and lists the consultant decisions that a proven change may invalidate. Results are a
/// replaceable snapshot per pair of runs; they never alter decisions.
/// </summary>
public sealed class RunComparisonService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope, DiscoveryCatalog catalog)
{
    private const string Compared = "Comparison.RunsCompared";

    public async Task<IReadOnlyList<ComparableRun>> RunsAsync(Guid projectId, Guid environmentId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, token);
        var ws = project.WorkspaceId;
        return await db.CollectionRuns.AsNoTracking()
            .Where(r => r.WorkspaceId == ws && r.ProjectId == project.Id && r.EnvironmentId == environment.Id)
            .Select(r => new
            {
                r,
                Sections = db.SectionRuns.Count(s => s.WorkspaceId == ws && s.RunId == r.Id && s.PublishedAtUtc != null)
            })
            .Where(x => x.Sections > 0).OrderByDescending(x => x.r.StartedAtUtc).ThenBy(x => x.r.Id).Take(100)
            .Select(x => new ComparableRun(x.r.Id, x.r.ExternalRunId, x.r.ProducerId, x.r.ProducerVersion, x.r.Channel, x.r.StartedAtUtc,
                x.r.CompletedAtUtc, x.Sections)).ToListAsync(token);
    }

    public async Task<Guid> CompareAsync(Guid projectId, Guid environmentId, Guid baselineRunId, Guid laterRunId, Guid operationId,
        CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        if (baselineRunId == laterRunId) { throw new ArgumentException("Choose two different collections to compare."); }
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var ws = project.WorkspaceId;
        var replay = await db.RunComparisons.AsNoTracking().SingleOrDefaultAsync(c => c.WorkspaceId == ws && c.OperationId == operationId, token);
        if (replay is not null)
        {
            if (replay.ProjectId != project.Id || replay.EnvironmentId != environmentId || replay.BaselineRunId != baselineRunId
                || replay.LaterRunId != laterRunId || replay.ActorTenantId != scope.Identity.TenantId
                || replay.ActorObjectId != scope.Identity.ObjectId) { throw new ConflictException(); }
            return replay.Id;
        }
        if (await db.AuditEvents.AnyAsync(a => a.WorkspaceId == ws && a.ResourceId == operationId, token)) { throw new ConflictException(); }
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, token);
        var env = environment.Id;
        var runs = await db.CollectionRuns.AsNoTracking().Where(r => r.WorkspaceId == ws && r.ProjectId == project.Id && r.EnvironmentId == env
            && (r.Id == baselineRunId || r.Id == laterRunId)).ToDictionaryAsync(r => r.Id, token);
        if (!runs.TryGetValue(baselineRunId, out var baseline) || !runs.TryGetValue(laterRunId, out var later)) { throw new AccessDeniedException(); }
        if (baseline.StartedAtUtc >= later.StartedAtUtc) { throw new ArgumentException("The baseline collection must start before the later one."); }

        var before = await LoadAsync(db, ws, baseline, token);
        var after = await LoadAsync(db, ws, later, token);
        var result = RunComparisonEngine.Compare(before, after);

        var names = new Dictionary<Guid, string?>();
        foreach (var chunk in result.Items.Select(i => i.ObjectId).Chunk(500))
        {
            foreach (var item in await db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == ws && i.EnvironmentId == env && chunk.Contains(i.Id))
                .Select(i => new { i.Id, Name = i.DisplayName ?? i.UserPrincipalName }).ToListAsync(token))
            { names[item.Id] = item.Name; }
        }
        var comparison = new RunComparison
        {
            WorkspaceId = ws,
            ProjectId = project.Id,
            EnvironmentId = env,
            BaselineRunId = baseline.Id,
            LaterRunId = later.Id,
            OperationId = operationId,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            ComputedAtUtc = DateTimeOffset.UtcNow,
            Added = result.Count(RunChange.Added),
            Modified = result.Count(RunChange.Modified),
            Removed = result.Count(RunChange.Removed),
            NewlyObserved = result.Count(RunChange.NewlyObserved),
            NotObserved = result.Count(RunChange.NotObserved),
            NotCompared = result.Count(RunChange.NotCompared),
            Unchanged = result.Count(RunChange.Unchanged),
            RelationsAdded = result.Units.Sum(u => u.RelationsAdded),
            RelationsRemoved = result.Units.Sum(u => u.RelationsRemoved),
            RelationsUnknown = result.Units.Sum(u => u.RelationsUnknown),
            UnitsJson = JsonSerializer.Serialize(result.Units, ContractJson.Options)
        };
        var previous = db.RunComparisons.Where(c => c.WorkspaceId == ws && c.ProjectId == project.Id && c.EnvironmentId == env
            && c.BaselineRunId == baseline.Id && c.LaterRunId == later.Id);
        await db.RunComparisonItems.Where(i => i.WorkspaceId == ws && previous.Any(c => c.Id == i.ComparisonId)).ExecuteDeleteAsync(token);
        await previous.ExecuteDeleteAsync(token);
        db.RunComparisons.Add(comparison);
        db.RunComparisonItems.AddRange(result.Items.Select(i =>
        {
            var name = names.GetValueOrDefault(i.ObjectId);
            var fields = string.Join(", ", i.ChangedFields);
            return new RunComparisonItem
            {
                WorkspaceId = ws,
                ComparisonId = comparison.Id,
                EnvironmentId = env,
                ObjectId = i.ObjectId,
                Type = i.Type,
                DisplayName = name is { Length: > 400 } ? name[..400] : name,
                Change = i.Change,
                Tombstone = i.Tombstone,
                ChangedFields = fields.Length > 1000 ? fields[..999] + "…" : fields,
                ChangesJson = JsonSerializer.Serialize(i.Changes, ContractJson.Options),
                ScopeReview = i.ScopeReview,
                MappingReview = i.MappingReview,
                AssessmentReview = i.AssessmentReview
            };
        }));
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = ws,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            Action = Compared,
            ResourceId = operationId,
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return comparison.Id;
    }

    public async Task<IReadOnlyList<ComparisonSummary>> ComparisonsAsync(Guid projectId, EnvironmentRole side, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var rows = await (from c in db.RunComparisons.AsNoTracking()
                          join e in db.Environments on new { c.WorkspaceId, Id = c.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                          join b in db.CollectionRuns on new { c.WorkspaceId, Id = c.BaselineRunId } equals new { b.WorkspaceId, b.Id }
                          join l in db.CollectionRuns on new { c.WorkspaceId, Id = c.LaterRunId } equals new { l.WorkspaceId, l.Id }
                          where c.WorkspaceId == project.WorkspaceId && c.ProjectId == project.Id && e.Role == side
                          orderby c.ComputedAtUtc descending, c.Id
                          select new { c, e.ExternalId, Baseline = b.StartedAtUtc, Later = l.StartedAtUtc }).Take(50).ToListAsync(token);
        return rows.Select(r => new ComparisonSummary(r.c.Id, r.c.EnvironmentId, r.ExternalId, r.c.BaselineRunId, r.Baseline, r.c.LaterRunId, r.Later,
            r.c.ComputedAtUtc, r.c.ActorObjectId, r.c.Added, r.c.Modified, r.c.Removed, r.c.NewlyObserved, r.c.NotObserved, r.c.NotCompared,
            r.c.Unchanged, r.c.RelationsAdded, r.c.RelationsRemoved, r.c.RelationsUnknown,
            JsonSerializer.Deserialize<List<ComparisonUnit>>(r.c.UnitsJson, ContractJson.Options)!)).ToList();
    }

    public async Task<PageResult<ComparisonItemRow>> ItemsAsync(Guid projectId, Guid comparisonId, RunChange? change, bool reviewOnly, PageRequest page,
        CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var comparison = await RequireAsync(db, project, comparisonId, token);
        var term = page.Search.Trim();
        var ws = project.WorkspaceId;
        var query = from i in db.RunComparisonItems.AsNoTracking()
                    where i.WorkspaceId == ws && i.ComparisonId == comparison.Id && (change == null || i.Change == change)
                        && (term == "" || i.DisplayName!.Contains(term))
                    select new
                    {
                        i,
                        Affected = i.ScopeReview && db.ScopeDecisions.Any(d => d.WorkspaceId == ws && d.ProjectId == project.Id
                                && d.EnvironmentId == i.EnvironmentId && d.ObjectId == i.ObjectId)
                            || i.MappingReview && db.MappingDecisions.Any(m => m.WorkspaceId == ws && m.ProjectId == project.Id
                                && (m.AdObjectId == i.ObjectId || m.EntraObjectId == i.ObjectId))
                            || i.AssessmentReview && db.Assessments.Any(a => a.WorkspaceId == ws && a.ProjectId == project.Id
                                && a.EnvironmentId == i.EnvironmentId && a.ObjectId == i.ObjectId)
                    };
        if (reviewOnly) { query = query.Where(x => x.Affected); }
        var rows = await query.OrderBy(x => x.i.Change).ThenBy(x => x.i.DisplayName).ThenBy(x => x.i.ObjectId).Skip(page.Start).Take(page.Count)
            .ToListAsync(token);
        return new(rows.Select(x => new ComparisonItemRow(x.i.ObjectId, x.i.Type, x.i.DisplayName, x.i.Change, x.i.Tombstone, x.i.ChangedFields,
            JsonSerializer.Deserialize<List<FieldChange>>(x.i.ChangesJson, ContractJson.Options)!, x.i.ScopeReview, x.i.MappingReview,
            x.i.AssessmentReview, x.Affected)).ToList(), await query.CountAsync(token));
    }

    /// <summary>Live decisions whose review the comparison calls for: scope decisions, mappings, then each current assessment.</summary>
    public async Task<PageResult<ComparisonDecisionRow>> DecisionsAsync(Guid projectId, Guid comparisonId, PageRequest page,
        CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var comparison = await RequireAsync(db, project, comparisonId, token);
        var ws = project.WorkspaceId;
        var items = db.RunComparisonItems.AsNoTracking().Where(i => i.WorkspaceId == ws && i.ComparisonId == comparison.Id);
        var scopes = from i in items
                     where i.ScopeReview
                     join d in db.ScopeDecisions on new { i.WorkspaceId, i.EnvironmentId, i.ObjectId } equals new { d.WorkspaceId, d.EnvironmentId, d.ObjectId }
                     where d.ProjectId == project.Id
                     orderby i.DisplayName, i.ObjectId
                     select new { i.ObjectId, i.DisplayName, i.Type, i.Change, i.ChangedFields, d.Value, d.Wave };
        var mappings = from i in items
                       where i.MappingReview
                       from m in db.MappingDecisions.Where(m => m.WorkspaceId == ws && m.ProjectId == project.Id
                           && (m.AdObjectId == i.ObjectId || m.EntraObjectId == i.ObjectId))
                       orderby i.DisplayName, i.ObjectId, m.Id
                       select new { i.ObjectId, i.DisplayName, i.Type, i.Change, i.ChangedFields, m.State, m.Rule };
        var assessments = from i in items
                          where i.AssessmentReview
                          join a in db.Assessments on new { i.WorkspaceId, i.EnvironmentId, i.ObjectId } equals new { a.WorkspaceId, a.EnvironmentId, a.ObjectId }
                          where a.ProjectId == project.Id && !db.Assessments.Any(b => b.WorkspaceId == ws && b.ProjectId == a.ProjectId
                              && b.EnvironmentId == a.EnvironmentId && b.ObjectId == a.ObjectId && b.Kind == a.Kind && b.Revision > a.Revision)
                          orderby i.DisplayName, i.ObjectId, a.Kind
                          select new { i.ObjectId, i.DisplayName, i.Type, i.Change, i.ChangedFields, a.Kind, a.Value, a.ScopeValue };
        var counts = (Scope: await scopes.CountAsync(token), Mapping: await mappings.CountAsync(token), Assessment: await assessments.CountAsync(token));
        var rows = new List<ComparisonDecisionRow>();
        var start = page.Start;
        var take = page.Count;
        if (start < counts.Scope)
        {
            rows.AddRange((await scopes.Skip(start).Take(take).ToListAsync(token)).Select(x => new ComparisonDecisionRow(DecisionKind.Scope, x.ObjectId,
                x.DisplayName, x.Type, x.Change, x.ChangedFields, $"ScopeDecision_{x.Value}", x.Wave)));
        }
        start = Math.Max(0, start - counts.Scope);
        take = page.Count - rows.Count;
        if (take > 0 && start < counts.Mapping)
        {
            rows.AddRange((await mappings.Skip(start).Take(take).ToListAsync(token)).Select(x => new ComparisonDecisionRow(DecisionKind.Mapping, x.ObjectId,
                x.DisplayName, x.Type, x.Change, x.ChangedFields, x.State.ToString(), x.Rule.ToString())));
        }
        start = Math.Max(0, start - counts.Mapping);
        take = page.Count - rows.Count;
        if (take > 0 && start < counts.Assessment)
        {
            rows.AddRange((await assessments.Skip(start).Take(take).ToListAsync(token)).Select(x => new ComparisonDecisionRow(
                x.Kind switch
                {
                    AssessmentKind.SsoDependency => DecisionKind.SsoDependency,
                    AssessmentKind.TeamsChatScope => DecisionKind.TeamsChatScope,
                    _ => DecisionKind.TeamsTelephonyScope
                }, x.ObjectId, x.DisplayName, x.Type, x.Change, x.ChangedFields,
                x.Kind == AssessmentKind.SsoDependency ? $"SsoDependency_{x.Value}" : $"QualificationScope_{x.ScopeValue}", null)));
        }
        return new(rows, counts.Scope + counts.Mapping + counts.Assessment);
    }

    private static async Task<RunComparison> RequireAsync(ClarityDbContext db, Project project, Guid comparisonId, CancellationToken token) =>
        await db.RunComparisons.AsNoTracking().SingleOrDefaultAsync(c => c.WorkspaceId == project.WorkspaceId && c.ProjectId == project.Id
            && c.Id == comparisonId, token) ?? throw new AccessDeniedException();

    private async Task<ComparedRun> LoadAsync(ClarityDbContext db, Guid ws, CollectionRun run, CancellationToken token)
    {
        var sections = await db.SectionRuns.AsNoTracking().Where(s => s.WorkspaceId == ws && s.RunId == run.Id && s.PublishedAtUtc != null)
            .ToListAsync(token);
        if (sections.Count == 0) { throw new ArgumentException("Both collections must have published sections."); }
        var compared = sections.Select(s =>
        {
            var projected = new ProjectedSection(s, run.ExternalRunId, run.ProducerId, run.ProducerVersion, run.StartedAtUtc, run.CompletedAtUtc,
                run.ConnectionId, run.Channel);
            var result = JsonSerializer.Deserialize<SectionResult>(s.ResultJson, ContractJson.Options)!;
            var stream = projected.Stream;
            return new ComparedSection(s.Id, s.CapabilityId, s.ScopeHash, JsonSerializer.Deserialize<CollectionScope>(s.ScopeJson, ContractJson.Options)!,
                $"{stream.Item1}|{stream.Item2}|{stream.Item3}|{stream.Item4}|{stream.Item5}", projected.IsExhaustive(catalog),
                s.Coverage == CoverageStatus.Complete && result.Warnings.Count == 0 && result.Errors.Count == 0, s.Coverage,
                result.CoveredFields.ToHashSet(StringComparer.Ordinal));
        }).ToList();
        var ids = compared.Select(s => s.Id).ToList();
        var observations = db.ObjectObservations.AsNoTracking().Where(o => o.WorkspaceId == ws && ids.Contains(o.SectionRunId));
        var relations = db.RelationshipObservations.AsNoTracking().Where(r => r.WorkspaceId == ws && ids.Contains(r.SectionRunId));
        if (await observations.CountAsync(token) > RunComparisonRules.MaxObservations
            || await relations.CountAsync(token) > RunComparisonRules.MaxRelations)
        {
            throw new ArgumentException("The collections are too large for one comparison.");
        }
        var tombstones = compared.Where(s => catalog.Sections.TryGetValue(s.CapabilityId, out var capability)
            && capability.Attributes.TryGetValue("deleted", out var field) && field.ReferenceProducer == run.ProducerId).Select(s => s.Id).ToHashSet();
        var objects = (await (from o in observations
                              join i in db.InventoryObjects on new { o.WorkspaceId, Id = o.ObjectId } equals new { i.WorkspaceId, i.Id }
                              select new { o.SectionRunId, o.ObjectId, i.Type, o.ObservedAtUtc, o.AttributesJson }).ToListAsync(token))
            .Select(o =>
            {
                var attributes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(o.AttributesJson, ContractJson.Options) ?? [];
                var tombstone = tombstones.Contains(o.SectionRunId) && attributes.TryGetValue("deleted", out var deleted)
                    && deleted.ValueKind == JsonValueKind.True;
                return new ComparedObject(o.SectionRunId, o.ObjectId, o.Type, o.ObservedAtUtc, attributes, tombstone);
            }).ToList();
        var hashes = await relations.Select(r => new ComparedRelation(r.SectionRunId, r.IdentityHash)).ToListAsync(token);
        return new(compared, objects, hashes);
    }
}
