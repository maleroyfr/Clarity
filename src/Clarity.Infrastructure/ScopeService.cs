using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record PopulationInput(Guid OperationId, Guid EnvironmentId, string Name, PopulationKind Kind, string Criterion = "",
    Guid? RootObjectId = null, string? ObjectType = null, string? Status = null);
public sealed record ImportInput(Guid OperationId, Guid EnvironmentId, string Name, ImportIdentifier Identifier, string? ObjectType, string Values);
public sealed record PopulationRow(Guid Id, Guid EnvironmentId, string Environment, string Name, PopulationKind Kind, string Criterion,
    string? ObjectType, string? Status, ImportIdentifier? Identifier, int MemberCount, DateTimeOffset EvaluatedAtUtc, int Unresolved);
public sealed record ScopeObjectRow(Guid ObjectId, Guid EnvironmentId, string? DisplayName, string? Upn, string Type, InventoryPresence Presence,
    Guid? DecisionId, ScopeValue? Value, string? Wave, string? Population, DateTimeOffset? UpdatedAtUtc, string? Fingerprint, bool NeedsReview = false);
public sealed record ImportEntryRow(Guid Id, int Line, string Value, ImportResolution Resolution, Guid? ObjectId, string? Label, int CandidateCount);
public sealed record ScopeCandidate(Guid ObjectId, string Label, string Type);
public sealed record ScopeBatch(Guid OperationId, ScopeValue Value, string? Wave, string Reason, Guid? PopulationId = null, IReadOnlyList<Guid>? ObjectIds = null);
public sealed record ScopePreview(int Members, int Affected, int Unchanged);
public sealed record ScopeCount(ScopeValue Value, string? Wave, int Count);
public sealed record ScopeSummary(IReadOnlyList<ScopeCount> Counts, int Populations);
public sealed record ScopeHistoryRow(long Number, ScopeValue Value, string? Wave, string? Population, string Reason, Guid ActorObjectId,
    DateTimeOffset OccurredAtUtc, Guid? EvidenceObservationId, Guid OperationId);

/// <summary>Populations, imported lists and historized consultant scope decisions (include, exclude, to arbitrate).</summary>
public sealed class ScopeService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope)
{
    private static readonly string[] Statuses = ["Enabled", "Disabled", nameof(InventoryPresence.Observed), nameof(InventoryPresence.AbsentFromScope),
        nameof(InventoryPresence.Unknown)];

    public async Task<ScopeSummary> SummaryAsync(Guid projectId, EnvironmentRole side, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var counts = await (from d in Decisions(db, project, side)
                            group d by new { d.Value, d.Wave } into g
                            select new ScopeCount(g.Key.Value, g.Key.Wave, g.Count())).ToListAsync(token);
        var populations = await (from p in db.Populations
                                 join e in db.Environments on new { p.WorkspaceId, Id = p.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                                 where p.WorkspaceId == project.WorkspaceId && p.ProjectId == project.Id && e.Role == side
                                 select p.Id).CountAsync(token);
        return new(counts.OrderBy(x => x.Value).ThenBy(x => x.Wave, StringComparer.Ordinal).ToList(), populations);
    }

    public async Task<PageResult<PopulationRow>> PopulationsAsync(Guid projectId, EnvironmentRole side, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var term = page.Search.Trim();
        var query = from p in db.Populations.AsNoTracking()
                    join e in db.Environments on new { p.WorkspaceId, Id = p.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                    where p.WorkspaceId == project.WorkspaceId && p.ProjectId == project.Id && e.Role == side
                        && (term == "" || p.Name.Contains(term))
                    select p;
        var ids = await query.OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Id).Skip(page.Start).Take(page.Count).Select(p => p.Id).ToListAsync(token);
        var found = await Rows(db, db.Populations.AsNoTracking().Where(p => p.WorkspaceId == project.WorkspaceId && ids.Contains(p.Id)))
            .ToDictionaryAsync(r => r.Id, token);
        return new(ids.Select(id => found[id]).ToList(), await query.CountAsync(token));
    }

    public async Task<PopulationRow> GetPopulationAsync(Guid projectId, Guid populationId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        return await Rows(db, db.Populations.AsNoTracking().Where(p => p.WorkspaceId == project.WorkspaceId && p.ProjectId == project.Id && p.Id == populationId))
            .SingleOrDefaultAsync(token) ?? throw new AccessDeniedException();
    }

    private static IQueryable<PopulationRow> Rows(ClarityDbContext db, IQueryable<Population> populations) =>
        from p in populations
        join e in db.Environments on new { p.WorkspaceId, Id = p.EnvironmentId } equals new { e.WorkspaceId, e.Id }
        select new PopulationRow(p.Id, p.EnvironmentId, e.ExternalId, p.Name, p.Kind, p.Criterion, p.ObjectType, p.Status,
            p.Identifier, p.MemberCount, p.EvaluatedAtUtc,
            db.PopulationEntries.Count(n => n.WorkspaceId == p.WorkspaceId && n.PopulationId == p.Id
                && (n.Resolution == ImportResolution.Ambiguous || n.Resolution == ImportResolution.Unmatched)));

    public async Task<IReadOnlyList<ScopeCandidate>> GroupsAsync(Guid projectId, Guid environmentId, string search, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        if (search.Length > 200) { throw new ArgumentException("Search is limited to 200 characters."); }
        await using var db = await factory.CreateDbContextAsync(token);
        var environment = await ImportService.EnvironmentAsync(db, project, environmentId, token);
        return await InventoryQueries.Filter(db, project, new(environment.Role, environment.Id, Type: "group"), search)
            .Where(i => i.Presence != InventoryPresence.Deleted).OrderBy(i => i.DisplayName).ThenBy(i => i.Id).Take(20)
            .Select(i => new ScopeCandidate(i.Id, i.DisplayName ?? i.Smtp ?? i.Id.ToString(), i.Type)).ToListAsync(token);
    }

    public async Task<Guid> CreatePopulationAsync(Guid projectId, PopulationInput input, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        if (input.OperationId == Guid.Empty || !Enum.IsDefined(input.Kind) || input.Kind == PopulationKind.ImportedList)
        { throw new ArgumentException("Invalid population."); }
        var criterion = (input.Criterion ?? "").Trim();
        var type = Optional(input.ObjectType);
        var status = Optional(input.Status);
        if (status != null && !Statuses.Contains(status)) { throw new ArgumentException("Unknown inventory status."); }
        if (input.Kind is PopulationKind.Domain or PopulationKind.Site && criterion.Length == 0
            || input.Kind == PopulationKind.OrganizationalUnit && !criterion.Contains('=', StringComparison.Ordinal)
            || input.Kind == PopulationKind.Filter && criterion.Length == 0 && type == null && status == null
            || (input.Kind == PopulationKind.Group) != input.RootObjectId.HasValue)
        { throw new ArgumentException("The population criterion is incomplete."); }
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var environment = await ImportService.EnvironmentAsync(db, project, input.EnvironmentId, token);
        var replay = await db.Populations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == input.OperationId, token);
        if (replay != null)
        {
            if (replay.ProjectId != project.Id || replay.EnvironmentId != environment.Id || replay.Name != input.Name.Trim() || replay.Kind != input.Kind
                || replay.RootObjectId != input.RootObjectId || replay.ObjectType != type || replay.Status != status
                || input.Kind != PopulationKind.Group && replay.Criterion != criterion)
            { throw new ConflictException(); }
            return replay.Id;
        }
        if (input.RootObjectId is { } root)
        {
            criterion = await db.InventoryObjects.Where(i => i.WorkspaceId == project.WorkspaceId && i.EnvironmentId == environment.Id && i.Id == root
                    && i.Type == "group" && i.Presence != InventoryPresence.Deleted)
                .Select(i => i.DisplayName ?? i.Smtp ?? i.Id.ToString()).SingleOrDefaultAsync(token) ?? throw new AccessDeniedException();
        }
        var population = new Population
        {
            Id = input.OperationId,
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            EnvironmentId = environment.Id,
            Name = input.Name.Trim(),
            Kind = input.Kind,
            Criterion = criterion,
            RootObjectId = input.RootObjectId,
            ObjectType = type,
            Status = status,
            AuthorTenantId = scope.Identity.TenantId,
            AuthorObjectId = scope.Identity.ObjectId
        };
        db.Populations.Add(population);
        await MaterializeAsync(db, project, environment, population, await EvaluateAsync(db, project, environment, population, token), token);
        Audit(db, project, "Scope.PopulationCreated", population.Id);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return population.Id;
    }

    public async Task<int> RefreshPopulationAsync(Guid projectId, Guid populationId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var population = await PopulationAsync(db, project, populationId, token);
        var environment = await ImportService.EnvironmentAsync(db, project, population.EnvironmentId, token);
        if (population.Kind == PopulationKind.ImportedList)
        {
            var entries = await db.PopulationEntries.Where(x => x.WorkspaceId == project.WorkspaceId && x.PopulationId == population.Id).ToListAsync(token);
            var live = await LiveAsync(db, population, entries.Where(x => x.ObjectId.HasValue).Select(x => x.ObjectId!.Value).ToList(), token);
            var stale = entries.Where(x => x.Resolution != ImportResolution.Chosen || !live.Contains(x.ObjectId!.Value)).ToList();
            await ResolveAsync(db, population, stale, token);
            await db.SaveChangesAsync(token);
        }
        await MaterializeAsync(db, project, environment, population, await EvaluateAsync(db, project, environment, population, token), token);
        Audit(db, project, "Scope.PopulationRefreshed", population.Id);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return population.MemberCount;
    }

    public async Task<Guid> ImportAsync(Guid projectId, ImportInput input, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        if (input.OperationId == Guid.Empty || !Enum.IsDefined(input.Identifier) || input.Values is null) { throw new ArgumentException("Invalid import."); }
        var lines = input.Values.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (lines.Count is 0 or > ScopeRules.MaxImportLines || lines.Any(x => x.Length > 500))
        { throw new ArgumentException($"Paste 1 to {ScopeRules.MaxImportLines} values of at most 500 characters."); }
        var type = Optional(input.ObjectType);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var environment = await ImportService.EnvironmentAsync(db, project, input.EnvironmentId, token);
        var replay = await db.Populations.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == input.OperationId, token);
        if (replay != null)
        {
            if (replay.ProjectId != project.Id || replay.EnvironmentId != environment.Id || replay.Kind != PopulationKind.ImportedList
                || replay.Name != input.Name.Trim() || replay.Identifier != input.Identifier || replay.ObjectType != type
                || await db.PopulationEntries.CountAsync(x => x.WorkspaceId == project.WorkspaceId && x.PopulationId == replay.Id, token) != lines.Count)
            { throw new ConflictException(); }
            return replay.Id;
        }
        var population = new Population
        {
            Id = input.OperationId,
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            EnvironmentId = environment.Id,
            Name = input.Name.Trim(),
            Kind = PopulationKind.ImportedList,
            Identifier = input.Identifier,
            ObjectType = type,
            AuthorTenantId = scope.Identity.TenantId,
            AuthorObjectId = scope.Identity.ObjectId
        };
        db.Populations.Add(population);
        var entries = lines.Select((value, index) => new PopulationEntry
        {
            WorkspaceId = project.WorkspaceId,
            PopulationId = population.Id,
            Line = index + 1,
            Value = value,
            Resolution = ImportResolution.Unmatched
        }).ToList();
        db.PopulationEntries.AddRange(entries);
        await ResolveAsync(db, population, entries, token);
        await MaterializeAsync(db, project, environment, population, entries.Where(x => x.ObjectId.HasValue).Select(x => x.ObjectId!.Value).Distinct().ToList(), token);
        Audit(db, project, "Scope.ListImported", population.Id);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return population.Id;
    }

    public async Task<PageResult<ImportEntryRow>> EntriesAsync(Guid projectId, Guid populationId, string resolution, PageRequest page,
        CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var population = await PopulationAsync(db, project, populationId, token);
        var entries = db.PopulationEntries.AsNoTracking().Where(x => x.WorkspaceId == project.WorkspaceId && x.PopulationId == population.Id);
        entries = resolution switch
        {
            "All" => entries,
            "Unresolved" => entries.Where(x => x.Resolution == ImportResolution.Ambiguous || x.Resolution == ImportResolution.Unmatched),
            _ when Enum.TryParse<ImportResolution>(resolution, out var value) && Enum.IsDefined(value) => entries.Where(x => x.Resolution == value),
            _ => throw new ArgumentException("Invalid resolution filter.")
        };
        var rows = await entries.OrderBy(x => x.Line).Skip(page.Start).Take(page.Count)
            .Select(x => new ImportEntryRow(x.Id, x.Line, x.Value, x.Resolution, x.ObjectId,
                db.InventoryObjects.Where(i => i.WorkspaceId == x.WorkspaceId && i.Id == x.ObjectId).Select(i => i.UserPrincipalName ?? i.DisplayName).FirstOrDefault(),
                x.CandidateCount)).ToListAsync(token);
        return new(rows, await entries.CountAsync(token));
    }

    public async Task<IReadOnlyList<ScopeCandidate>> CandidatesAsync(Guid projectId, Guid entryId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var (population, entry) = await EntryAsync(db, project, entryId, token);
        var ids = (await MatchAsync(db, population, [entry.Value], token)).GetValueOrDefault(entry.Value) ?? [];
        return await db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == project.WorkspaceId && ids.Contains(i.Id))
            .OrderBy(i => i.DisplayName).ThenBy(i => i.Id)
            .Select(i => new ScopeCandidate(i.Id, (i.DisplayName ?? i.Id.ToString()) + (i.UserPrincipalName == null ? "" : " · " + i.UserPrincipalName), i.Type))
            .ToListAsync(token);
    }

    public async Task ChooseAsync(Guid projectId, Guid entryId, Guid objectId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var (population, entry) = await EntryAsync(db, project, entryId, token);
        if (entry.Resolution == ImportResolution.Chosen && entry.ObjectId == objectId) { return; }
        var candidates = (await MatchAsync(db, population, [entry.Value], token)).GetValueOrDefault(entry.Value) ?? [];
        if (entry.Resolution is not (ImportResolution.Ambiguous or ImportResolution.Chosen) || !candidates.Contains(objectId))
        { throw new ConflictException(); }
        entry.Resolution = ImportResolution.Chosen;
        entry.ObjectId = objectId;
        entry.CandidateCount = candidates.Count;
        await db.SaveChangesAsync(token);
        var environment = await ImportService.EnvironmentAsync(db, project, population.EnvironmentId, token);
        await MaterializeAsync(db, project, environment, population, await EvaluateAsync(db, project, environment, population, token), token);
        Audit(db, project, "Scope.EntryResolved", entry.Id);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task<PageResult<ScopeObjectRow>> MembersAsync(Guid projectId, Guid populationId, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var population = await PopulationAsync(db, project, populationId, token);
        var term = page.Search.Trim().ToUpperInvariant();
        var query = from m in db.PopulationMembers.AsNoTracking()
                    join i in db.InventoryObjects on new { m.WorkspaceId, Id = m.ObjectId } equals new { i.WorkspaceId, i.Id }
                    where m.WorkspaceId == project.WorkspaceId && m.PopulationId == population.Id
                        && (term == "" || i.SearchName != null && i.SearchName.StartsWith(term))
                    select i;
        return await ObjectPageAsync(db, project, query, page, token);
    }

    public async Task<PageResult<ScopeObjectRow>> DecisionsAsync(Guid projectId, EnvironmentRole side, string value, PageRequest page,
        CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var decisions = Decisions(db, project, side);
        decisions = value switch
        {
            "All" => decisions,
            _ when Enum.TryParse<ScopeValue>(value, out var v) && Enum.IsDefined(v) => decisions.Where(x => x.Value == v),
            _ => throw new ArgumentException("Invalid scope decision filter.")
        };
        var term = page.Search.Trim().ToUpperInvariant();
        var query = from d in decisions
                    join i in db.InventoryObjects on new { d.WorkspaceId, Id = d.ObjectId } equals new { i.WorkspaceId, i.Id }
                    where term == "" || i.SearchName != null && i.SearchName.StartsWith(term) || d.Wave != null && d.Wave.StartsWith(term)
                    select i;
        return await ObjectPageAsync(db, project, query, page, token);
    }

    public async Task<PageResult<ScopeHistoryRow>> HistoryAsync(Guid projectId, Guid objectId, PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var query = from r in db.ScopeDecisionRevisions.AsNoTracking()
                    join d in db.ScopeDecisions on new { r.WorkspaceId, Id = r.DecisionId } equals new { d.WorkspaceId, d.Id }
                    where d.WorkspaceId == project.WorkspaceId && d.ProjectId == project.Id && d.ObjectId == objectId
                    select r;
        var rows = await query.OrderByDescending(r => r.Number).Skip(page.Start).Take(page.Count)
            .Select(r => new ScopeHistoryRow(r.Number, r.Value, r.Wave,
                db.Populations.Where(p => p.WorkspaceId == r.WorkspaceId && p.Id == r.PopulationId).Select(p => p.Name).FirstOrDefault(),
                r.Reason, r.ActorObjectId, r.OccurredAtUtc, r.EvidenceObservationId, r.OperationId)).ToListAsync(token);
        return new(rows, await query.CountAsync(token));
    }

    public async Task<ScopePreview> PreviewAsync(Guid projectId, ScopeBatch batch, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        var wave = Validate(batch);
        await using var db = await factory.CreateDbContextAsync(token);
        var (targets, changed, _) = await AffectedAsync(db, project, batch, wave, token);
        return new(targets.Count, changed.Count, targets.Count - changed.Count);
    }

    public async Task ApplyAsync(Guid projectId, ScopeBatch batch, int expectedAffected, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        var wave = Validate(batch);
        InventoryMatching.ValidateReason(batch.Reason);
        var reason = batch.Reason.Trim();
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var replay = await (from r in db.ScopeDecisionRevisions
                            join d in db.ScopeDecisions on new { r.WorkspaceId, Id = r.DecisionId } equals new { d.WorkspaceId, d.Id }
                            where r.WorkspaceId == project.WorkspaceId && r.OperationId == batch.OperationId
                            select new { r, d.ProjectId }).ToListAsync(token);
        if (replay.Count != 0)
        {
            if (replay.Count != expectedAffected || replay.Any(x => x.ProjectId != project.Id || x.r.Value != batch.Value || x.r.Wave != wave
                || x.r.PopulationId != batch.PopulationId || x.r.Reason != reason || x.r.ActorTenantId != scope.Identity.TenantId
                || x.r.ActorObjectId != scope.Identity.ObjectId))
            { throw new ConflictException(); }
            return;
        }
        var (_, changed, existing) = await AffectedAsync(db, project, batch, wave, token);
        if (changed.Count != expectedAffected) { throw new ConflictException(); }
        var ids = changed.Select(x => x.ObjectId).ToList();
        var fingerprints = await FingerprintsAsync(db, project.WorkspaceId, ids, token);
        var evidence = await EvidenceAsync(db, project.WorkspaceId, ids, token);
        var now = DateTimeOffset.UtcNow;
        foreach (var target in changed)
        {
            if (!existing.TryGetValue(target.ObjectId, out var decision))
            {
                decision = new ScopeDecision
                {
                    WorkspaceId = project.WorkspaceId,
                    ProjectId = project.Id,
                    EnvironmentId = target.EnvironmentId,
                    ObjectId = target.ObjectId,
                    Fingerprint = fingerprints[target.ObjectId]
                };
                db.ScopeDecisions.Add(decision);
            }
            decision.Value = batch.Value;
            decision.Wave = wave;
            decision.PopulationId = batch.PopulationId;
            decision.Fingerprint = fingerprints[target.ObjectId];
            decision.Revision++;
            decision.UpdatedAtUtc = now;
            db.ScopeDecisionRevisions.Add(new ScopeDecisionRevision
            {
                WorkspaceId = project.WorkspaceId,
                DecisionId = decision.Id,
                OperationId = batch.OperationId,
                Number = decision.Revision,
                Value = batch.Value,
                Wave = wave,
                PopulationId = batch.PopulationId,
                Fingerprint = decision.Fingerprint,
                EvidenceObservationId = evidence.GetValueOrDefault(target.ObjectId),
                Reason = reason,
                ActorTenantId = scope.Identity.TenantId,
                ActorObjectId = scope.Identity.ObjectId,
                OccurredAtUtc = now
            });
        }
        if (changed.Count != 0) { Audit(db, project, "Scope.DecisionsApplied", batch.OperationId); }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    internal static async Task<Dictionary<Guid, string>> FingerprintsAsync(ClarityDbContext db, Guid workspace, IReadOnlyCollection<Guid> ids,
        CancellationToken token)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var chunk in ids.Distinct().Chunk(500))
        {
            var items = await db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == workspace && chunk.Contains(i.Id))
                .Select(i => new { i.Id, i.Presence }).ToListAsync(token);
            var values = (await db.CurrentValues.AsNoTracking().Where(v => v.WorkspaceId == workspace && chunk.Contains(v.ObjectId)
                    && ScopeRules.CriticalFields.Contains(v.Field))
                .Select(v => new { v.ObjectId, v.Field, v.ValueJson, v.Conflicting }).ToListAsync(token)).ToLookup(v => v.ObjectId);
            foreach (var item in items)
            {
                var fields = values[item.Id].OrderBy(v => v.Field, StringComparer.Ordinal).ThenBy(v => v.ValueJson, StringComparer.Ordinal)
                    .Select(v => new { v.Field, v.ValueJson, v.Conflicting }).ToList();
                result[item.Id] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { item.Presence, fields }))));
            }
        }
        return result;
    }

    private static async Task<Dictionary<Guid, Guid>> EvidenceAsync(ClarityDbContext db, Guid workspace, IReadOnlyCollection<Guid> ids, CancellationToken token)
    {
        var result = new Dictionary<Guid, Guid>();
        foreach (var chunk in ids.Chunk(500))
        {
            var observations = await (from o in db.ObjectObservations.AsNoTracking()
                                      join s in db.SectionRuns on new { o.WorkspaceId, Id = o.SectionRunId } equals new { s.WorkspaceId, s.Id }
                                      where o.WorkspaceId == workspace && chunk.Contains(o.ObjectId) && s.PublishedAtUtc != null
                                      select new { o.ObjectId, o.Id, o.ObservedAtUtc }).ToListAsync(token);
            foreach (var latest in observations.GroupBy(o => o.ObjectId))
            { result[latest.Key] = latest.OrderByDescending(o => o.ObservedAtUtc).ThenByDescending(o => o.Id).First().Id; }
        }
        return result;
    }

    private async Task<PageResult<ScopeObjectRow>> ObjectPageAsync(ClarityDbContext db, Project project, IQueryable<InventoryObject> objects,
        PageRequest page, CancellationToken token)
    {
        var ordered = page.Descending ? objects.OrderByDescending(i => i.DisplayName) : objects.OrderBy(i => i.DisplayName);
        var items = await ordered.ThenBy(i => i.Id).Skip(page.Start).Take(page.Count)
            .Select(i => new { i.Id, i.EnvironmentId, i.DisplayName, i.UserPrincipalName, i.Type, i.Presence }).ToListAsync(token);
        var ids = items.Select(i => i.Id).ToArray();
        var decisions = await db.ScopeDecisions.AsNoTracking().Where(d => d.WorkspaceId == project.WorkspaceId && d.ProjectId == project.Id
                && ids.Contains(d.ObjectId))
            .Select(d => new
            {
                d.ObjectId,
                d.Id,
                d.Value,
                d.Wave,
                d.UpdatedAtUtc,
                d.Fingerprint,
                Population = db.Populations.Where(p => p.WorkspaceId == d.WorkspaceId && p.Id == d.PopulationId).Select(p => p.Name).FirstOrDefault()
            }).ToDictionaryAsync(d => d.ObjectId, token);
        var current = await FingerprintsAsync(db, project.WorkspaceId, decisions.Keys, token);
        var rows = items.Select(i => decisions.TryGetValue(i.Id, out var d)
            ? new ScopeObjectRow(i.Id, i.EnvironmentId, i.DisplayName, i.UserPrincipalName, i.Type, i.Presence, d.Id, d.Value, d.Wave, d.Population,
                d.UpdatedAtUtc, d.Fingerprint, current.GetValueOrDefault(i.Id) != d.Fingerprint)
            : new ScopeObjectRow(i.Id, i.EnvironmentId, i.DisplayName, i.UserPrincipalName, i.Type, i.Presence, null, null, null, null, null, null)).ToList();
        return new(rows, await objects.CountAsync(token));
    }

    private static IQueryable<ScopeDecision> Decisions(ClarityDbContext db, Project project, EnvironmentRole side) =>
        from d in db.ScopeDecisions.AsNoTracking()
        join e in db.Environments on new { d.WorkspaceId, Id = d.EnvironmentId } equals new { e.WorkspaceId, e.Id }
        where d.WorkspaceId == project.WorkspaceId && d.ProjectId == project.Id && e.Role == side
        select d;

    private static async Task<Population> PopulationAsync(ClarityDbContext db, Project project, Guid id, CancellationToken token) =>
        await db.Populations.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == project.Id && x.Id == id, token)
        ?? throw new AccessDeniedException();

    private static async Task<(Population, PopulationEntry)> EntryAsync(ClarityDbContext db, Project project, Guid entryId, CancellationToken token)
    {
        var entry = await db.PopulationEntries.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == entryId, token)
            ?? throw new AccessDeniedException();
        return (await PopulationAsync(db, project, entry.PopulationId, token), entry);
    }

    private static string? Validate(ScopeBatch batch)
    {
        if (batch.OperationId == Guid.Empty || !Enum.IsDefined(batch.Value) || batch.PopulationId.HasValue == (batch.ObjectIds != null)
            || batch.ObjectIds is { } ids && (ids.Count is < 1 or > 100 || ids.Distinct().Count() != ids.Count))
        { throw new ArgumentException("Select one population or 1 to 100 distinct objects."); }
        return ScopeRules.Wave(batch.Wave);
    }

    private static async Task<(List<(Guid ObjectId, Guid EnvironmentId)> Targets, List<(Guid ObjectId, Guid EnvironmentId)> Changed,
        Dictionary<Guid, ScopeDecision> Existing)> AffectedAsync(ClarityDbContext db, Project project, ScopeBatch batch, string? wave, CancellationToken token)
    {
        var workspace = project.WorkspaceId;
        List<(Guid ObjectId, Guid EnvironmentId)> targets;
        if (batch.PopulationId is { } populationId)
        {
            var population = await PopulationAsync(db, project, populationId, token);
            targets = (await (from m in db.PopulationMembers
                              join i in db.InventoryObjects on new { m.WorkspaceId, Id = m.ObjectId } equals new { i.WorkspaceId, i.Id }
                              where m.WorkspaceId == workspace && m.PopulationId == population.Id && i.Presence != InventoryPresence.Deleted
                              select new { i.Id, i.EnvironmentId }).ToListAsync(token)).Select(x => (x.Id, x.EnvironmentId)).ToList();
        }
        else
        {
            var ids = batch.ObjectIds!.ToArray();
            targets = (await (from i in db.InventoryObjects
                              join e in db.Environments on new { i.WorkspaceId, Id = i.EnvironmentId } equals new { e.WorkspaceId, e.Id }
                              where i.WorkspaceId == workspace && e.ProjectId == project.Id && ids.Contains(i.Id) && i.Presence != InventoryPresence.Deleted
                              select new { i.Id, i.EnvironmentId }).ToListAsync(token)).Select(x => (x.Id, x.EnvironmentId)).ToList();
            if (targets.Count != ids.Length) { throw new AccessDeniedException(); }
        }
        var existing = new Dictionary<Guid, ScopeDecision>();
        foreach (var chunk in targets.Select(x => x.ObjectId).Chunk(500))
        {
            foreach (var decision in await db.ScopeDecisions.Where(d => d.WorkspaceId == workspace && d.ProjectId == project.Id && chunk.Contains(d.ObjectId))
                .ToListAsync(token))
            { existing[decision.ObjectId] = decision; }
        }
        var changed = targets.Where(t => !existing.TryGetValue(t.ObjectId, out var d) || d.Value != batch.Value || d.Wave != wave).ToList();
        return (targets, changed, existing);
    }

    private static async Task<List<Guid>> EvaluateAsync(ClarityDbContext db, Project project, ProjectEnvironment environment, Population population,
        CancellationToken token)
    {
        var workspace = project.WorkspaceId;
        List<Guid> candidates;
        switch (population.Kind)
        {
            case PopulationKind.Group:
                candidates = await GroupMembersAsync(db, workspace, environment.Id, population.RootObjectId!.Value, token);
                break;
            case PopulationKind.OrganizationalUnit:
                var suffix = "," + population.Criterion.Trim().ToUpperInvariant();
                candidates = await db.AlternateKeyPeriods.AsNoTracking().Where(k => k.WorkspaceId == workspace && k.EnvironmentId == environment.Id
                        && k.Namespace == "ad.distinguishedName" && k.ValidUntilUtc == null && k.NormalizedValue.EndsWith(suffix))
                    .Select(k => k.ObjectId).Distinct().Take(ScopeRules.MaxPopulation * 5 + 1).ToListAsync(token);
                break;
            case PopulationKind.ImportedList:
                candidates = await db.PopulationEntries.AsNoTracking().Where(x => x.WorkspaceId == workspace && x.PopulationId == population.Id
                    && x.ObjectId != null).Select(x => x.ObjectId!.Value).Distinct().ToListAsync(token);
                break;
            default:
                var filter = new InventoryFilter(environment.Role, environment.Id,
                    Domain: population.Kind == PopulationKind.Domain ? population.Criterion : null,
                    Site: population.Kind == PopulationKind.Site ? population.Criterion : null, Type: population.ObjectType, Status: population.Status);
                var found = await InventoryQueries.Filter(db, project, filter, population.Kind == PopulationKind.Filter ? population.Criterion : "")
                    .Where(i => i.Presence != InventoryPresence.Deleted).Select(i => i.Id).Take(ScopeRules.MaxPopulation + 1).ToListAsync(token);
                return Bounded(found);
        }
        var members = new List<Guid>();
        if (candidates.Count > ScopeRules.MaxPopulation * 5) { Bounded(candidates); }
        var presence = population.Status is { } wanted && Enum.TryParse<InventoryPresence>(wanted, out var parsed) ? parsed : (InventoryPresence?)null;
        foreach (var chunk in candidates.Chunk(500))
        {
            var query = db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == workspace && i.EnvironmentId == environment.Id
                && chunk.Contains(i.Id) && i.Presence != InventoryPresence.Deleted);
            if (population.ObjectType != null) { query = query.Where(i => i.Type == population.ObjectType); }
            query = population.Status switch
            {
                null => query,
                "Enabled" => query.Where(i => i.Enabled == true),
                "Disabled" => query.Where(i => i.Enabled == false),
                _ => query.Where(i => i.Presence == presence)
            };
            members.AddRange(await query.Select(i => i.Id).ToListAsync(token));
            Bounded(members);
        }
        return members;
    }

    private static List<Guid> Bounded(List<Guid> members) => members.Count > ScopeRules.MaxPopulation
        ? throw new ArgumentException($"A population is limited to {ScopeRules.MaxPopulation} objects; refine the criterion.")
        : members;

    private static async Task<List<Guid>> GroupMembersAsync(ClarityDbContext db, Guid workspace, Guid environment, Guid root, CancellationToken token)
    {
        var seen = new HashSet<Guid> { root };
        var members = new List<Guid>();
        var frontier = new List<Guid> { root };
        while (frontier.Count != 0)
        {
            var next = new List<Guid>();
            foreach (var chunk in frontier.Chunk(500))
            {
                var found = await db.InventoryRelationships.AsNoTracking().Where(e => e.WorkspaceId == workspace && e.EnvironmentId == environment
                        && e.Origin == RelationshipOrigin.Membership && e.IsCurrent && e.FromObjectId != null && e.ToObjectId != null
                        && chunk.Contains(e.ToObjectId.Value))
                    .Select(e => e.FromObjectId!.Value).Distinct().ToListAsync(token);
                foreach (var id in found.Where(seen.Add)) { members.Add(id); next.Add(id); }
            }
            if (members.Count > ScopeRules.MaxPopulation * 5) { Bounded(members); }
            frontier = next;
        }
        return members;
    }

    private static async Task MaterializeAsync(ClarityDbContext db, Project project, ProjectEnvironment environment, Population population,
        List<Guid> members, CancellationToken token)
    {
        Bounded(members);
        await db.PopulationMembers.Where(x => x.WorkspaceId == project.WorkspaceId && x.PopulationId == population.Id).ExecuteDeleteAsync(token);
        db.PopulationMembers.AddRange(members.Distinct().Select(id => new PopulationMember
        {
            WorkspaceId = project.WorkspaceId,
            PopulationId = population.Id,
            EnvironmentId = environment.Id,
            ObjectId = id
        }));
        population.MemberCount = members.Distinct().Count();
        population.EvaluatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static async Task<HashSet<Guid>> LiveAsync(ClarityDbContext db, Population population, List<Guid> ids, CancellationToken token)
    {
        var live = new HashSet<Guid>();
        foreach (var chunk in ids.Chunk(500))
        {
            live.UnionWith(await db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == population.WorkspaceId
                && i.EnvironmentId == population.EnvironmentId && chunk.Contains(i.Id) && i.Presence != InventoryPresence.Deleted)
                .Select(i => i.Id).ToListAsync(token));
        }
        return live;
    }

    private static async Task ResolveAsync(ClarityDbContext db, Population population, List<PopulationEntry> entries, CancellationToken token)
    {
        var matches = await MatchAsync(db, population, entries.Select(x => x.Value).ToList(), token);
        foreach (var entry in entries)
        {
            var found = matches.GetValueOrDefault(entry.Value) ?? [];
            entry.CandidateCount = found.Count;
            entry.Resolution = found.Count switch { 0 => ImportResolution.Unmatched, 1 => ImportResolution.Matched, _ => ImportResolution.Ambiguous };
            entry.ObjectId = found.Count == 1 ? found[0] : null;
        }
    }

    /// <summary>Resolves imported values to live objects of the population environment by the chosen identifier.</summary>
    private static async Task<Dictionary<string, List<Guid>>> MatchAsync(ClarityDbContext db, Population population, IReadOnlyList<string> values,
        CancellationToken token)
    {
        var identifier = population.Identifier ?? throw new ConflictException();
        var workspace = population.WorkspaceId;
        var environment = population.EnvironmentId;
        var ns = identifier switch
        {
            ImportIdentifier.UserPrincipalName => "upn",
            ImportIdentifier.Smtp => "smtp",
            ImportIdentifier.SamAccountName => "sAMAccountName",
            _ => null
        };
        string Key(string value) => identifier switch
        {
            ImportIdentifier.SamAccountName when value.LastIndexOf('\\') is var slash and >= 0 => AlternateKeyProjection.Normalize(ns!, value[(slash + 1)..]),
            _ when ns != null => AlternateKeyProjection.Normalize(ns, value),
            _ => value.Trim().ToUpperInvariant()
        };
        var keys = values.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(v => v, Key, StringComparer.OrdinalIgnoreCase);
        var live = db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == workspace && i.EnvironmentId == environment
            && i.Presence != InventoryPresence.Deleted && (population.ObjectType == null || i.Type == population.ObjectType));
        var found = new List<(string Key, Guid Id)>();
        foreach (var chunk in keys.Values.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(500))
        {
            var rows = identifier switch
            {
                ImportIdentifier.ExternalId => await (from k in db.ExternalKeys
                                                      join i in live on new { k.WorkspaceId, Id = k.ObjectId } equals new { i.WorkspaceId, i.Id }
                                                      where k.WorkspaceId == workspace && k.EnvironmentId == environment && chunk.Contains(k.SearchId!)
                                                      select new { Key = k.SearchId!, i.Id }).Distinct().ToListAsync(token),
                ImportIdentifier.DisplayName => await live.Where(i => chunk.Contains(i.SearchName!)).Select(i => new { Key = i.SearchName!, i.Id })
                    .ToListAsync(token),
                _ => await (from k in db.AlternateKeyPeriods
                            join i in live on new { k.WorkspaceId, Id = k.ObjectId } equals new { i.WorkspaceId, i.Id }
                            where k.WorkspaceId == workspace && k.EnvironmentId == environment && k.Namespace == ns && k.ValidUntilUtc == null
                                && chunk.Contains(k.NormalizedValue)
                            select new { Key = k.NormalizedValue, i.Id }).Distinct().ToListAsync(token)
            };
            found.AddRange(rows.Select(r => (r.Key, r.Id)));
        }
        var byKey = found.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).Distinct().Order().ToList(), StringComparer.OrdinalIgnoreCase);
        return keys.ToDictionary(x => x.Key, x => byKey.GetValueOrDefault(x.Value) ?? [], StringComparer.OrdinalIgnoreCase);
    }

    private void Audit(ClarityDbContext db, Project project, string action, Guid resource) => db.AuditEvents.Add(new AuditEvent
    {
        WorkspaceId = project.WorkspaceId,
        ActorTenantId = scope.Identity.TenantId,
        ActorObjectId = scope.Identity.ObjectId,
        Action = action,
        ResourceId = resource,
        CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
    });

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { Length: <= 100 } trimmed
        ? trimmed : throw new ArgumentException("The value is limited to 100 characters.");
}
