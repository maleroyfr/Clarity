using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record DecisionKindCount(DecisionKind Kind, int Total, int ToReview);

/// <summary>
/// One current consultant decision. <see cref="ValueKey"/> is a localization key; <see cref="Changes"/> lists the critical fields that
/// changed since the decision and is null when they cannot be explained (mapping reviews, or decisions recorded before bases were kept).
/// </summary>
public sealed record DecisionRegisterRow(DecisionKind Kind, Guid ObjectId, Guid EnvironmentId, string Label, string Type, InventoryPresence Presence,
    string ValueKey, string? Detail, long Revision, Guid? AuthorObjectId, DateTimeOffset? DecidedAtUtc, string? Reason, bool NeedsReview,
    DateTimeOffset? FlaggedAtUtc, IReadOnlyList<BasisChange>? Changes, Guid? MappingId = null, ScopeValue? ScopeValue = null, string? Wave = null);

/// <summary>
/// Register of the current scope decisions, assessments and mapping decisions of a project, independent from collections. It lists the
/// decisions whose re-review conditions are met; reviewing them goes through each decision's own historized workflow.
/// </summary>
public sealed class DecisionRegisterService(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope)
{
    public async Task<IReadOnlyList<DecisionKindCount>> SummaryAsync(Guid projectId, EnvironmentRole side, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var objects = Objects(db, project, side, "");
        var result = new List<DecisionKindCount>();
        foreach (var kind in Enum.GetValues<DecisionKind>())
        {
            if (kind == DecisionKind.Mapping)
            {
                var mappings = from m in db.MappingDecisions.AsNoTracking()
                               join i in objects on new { m.WorkspaceId, Id = m.AdObjectId } equals new { i.WorkspaceId, i.Id }
                               where m.WorkspaceId == project.WorkspaceId && m.ProjectId == project.Id
                               select m.State;
                result.Add(new(kind, await mappings.CountAsync(token), await mappings.CountAsync(s => s == MappingState.ToReview, token)));
                continue;
            }
            var decided = kind == DecisionKind.Scope
                ? db.ScopeDecisions.Where(d => d.WorkspaceId == project.WorkspaceId && d.ProjectId == project.Id).Select(d => d.ObjectId)
                : db.Assessments.Where(a => a.WorkspaceId == project.WorkspaceId && a.ProjectId == project.Id && a.Kind == Assessment(kind))
                    .Select(a => a.ObjectId).Distinct();
            var total = await (from id in decided join i in objects on id equals i.Id select id).CountAsync(token);
            var flagged = await (from f in db.ReviewFlags
                                 join i in objects on new { f.WorkspaceId, Id = f.ObjectId } equals new { i.WorkspaceId, i.Id }
                                 where f.WorkspaceId == project.WorkspaceId && f.ProjectId == project.Id && f.Kind == kind
                                 select f.Id).CountAsync(token);
            result.Add(new(kind, total, flagged));
        }
        return result;
    }

    public async Task<PageResult<DecisionRegisterRow>> PageAsync(Guid projectId, EnvironmentRole side, DecisionKind kind, bool toReviewOnly,
        PageRequest page, CancellationToken token = default)
    {
        page.Validate();
        if (!Enum.IsDefined(kind)) { throw new ArgumentException("Invalid decision kind."); }
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var objects = Objects(db, project, side, page.Search.Trim().ToUpperInvariant());
        var rows = kind switch
        {
            DecisionKind.Scope => await ScopeAsync(db, project, objects, toReviewOnly, page, token),
            DecisionKind.Mapping => await MappingAsync(db, project, objects, toReviewOnly, page, token),
            _ => await AssessmentsAsync(db, project, objects, kind, toReviewOnly, page, token)
        };
        if (kind == DecisionKind.Mapping) { return rows; }
        var ids = rows.Items.Select(r => r.ObjectId).ToArray();
        var flags = await db.ReviewFlags.AsNoTracking()
            .Where(f => f.WorkspaceId == project.WorkspaceId && f.ProjectId == project.Id && f.Kind == kind && ids.Contains(f.ObjectId))
            .ToDictionaryAsync(f => f.ObjectId, token);
        return new(rows.Items.Select(r => flags.TryGetValue(r.ObjectId, out var flag)
            ? r with { NeedsReview = true, FlaggedAtUtc = flag.FlaggedAtUtc, Changes = DecisionReviewProjection.ReadChanges(flag.ChangesJson) }
            : r).ToList(), rows.TotalCount);
    }

    /// <summary>Recomputes every review flag of the project, for decisions recorded before bases were kept or after a manual check.</summary>
    public async Task<int> RecomputeAsync(Guid projectId, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var ids = await db.ScopeDecisions.Where(d => d.WorkspaceId == project.WorkspaceId && d.ProjectId == project.Id).Select(d => d.ObjectId)
            .Union(db.Assessments.Where(a => a.WorkspaceId == project.WorkspaceId && a.ProjectId == project.Id).Select(a => a.ObjectId))
            .Union(db.ReviewFlags.Where(f => f.WorkspaceId == project.WorkspaceId && f.ProjectId == project.Id).Select(f => f.ObjectId))
            .ToListAsync(token);
        var flagged = await DecisionReviewProjection.RefreshAsync(db, project.WorkspaceId, project.Id, ids, token);
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = project.WorkspaceId,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            Action = "DecisionReview.Recomputed",
            ResourceId = project.Id,
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return flagged;
    }

    private static AssessmentKind Assessment(DecisionKind kind) => kind switch
    {
        DecisionKind.SsoDependency => AssessmentKind.SsoDependency,
        DecisionKind.TeamsChatScope => AssessmentKind.TeamsChatScope,
        DecisionKind.TeamsTelephonyScope => AssessmentKind.TeamsTelephonyScope,
        _ => throw new ArgumentException("Not an assessment kind.", nameof(kind))
    };

    private static IQueryable<InventoryObject> Objects(ClarityDbContext db, Project project, EnvironmentRole side, string term) =>
        from i in db.InventoryObjects.AsNoTracking()
        join e in db.Environments on new { i.WorkspaceId, Id = i.EnvironmentId } equals new { e.WorkspaceId, e.Id }
        where i.WorkspaceId == project.WorkspaceId && e.ProjectId == project.Id && e.Role == side
            && (term == "" || i.SearchName != null && i.SearchName.StartsWith(term))
        select i;

    private static async Task<PageResult<DecisionRegisterRow>> ScopeAsync(ClarityDbContext db, Project project, IQueryable<InventoryObject> objects,
        bool toReviewOnly, PageRequest page, CancellationToken token)
    {
        var query = from d in db.ScopeDecisions.AsNoTracking()
                    join i in objects on new { d.WorkspaceId, Id = d.ObjectId } equals new { i.WorkspaceId, i.Id }
                    where d.WorkspaceId == project.WorkspaceId && d.ProjectId == project.Id
                        && (!toReviewOnly || db.ReviewFlags.Any(f => f.WorkspaceId == d.WorkspaceId && f.ProjectId == d.ProjectId
                            && f.Kind == DecisionKind.Scope && f.ObjectId == d.ObjectId))
                    select new { d, i };
        var items = await query.OrderBy(x => x.i.DisplayName).ThenBy(x => x.i.Id).Skip(page.Start).Take(page.Count)
            .Select(x => new
            {
                x.i.Id,
                x.i.EnvironmentId,
                Label = x.i.UserPrincipalName ?? x.i.DisplayName,
                x.i.Type,
                x.i.Presence,
                x.d.Value,
                x.d.Wave,
                x.d.Revision,
                x.d.UpdatedAtUtc,
                Last = db.ScopeDecisionRevisions.Where(r => r.WorkspaceId == x.d.WorkspaceId && r.DecisionId == x.d.Id && r.Number == x.d.Revision)
                    .Select(r => new { r.Reason, r.ActorObjectId }).FirstOrDefault()
            }).ToListAsync(token);
        return new(items.Select(x => new DecisionRegisterRow(DecisionKind.Scope, x.Id, x.EnvironmentId, x.Label ?? x.Id.ToString(), x.Type, x.Presence,
            $"ScopeDecision_{x.Value}", x.Wave, x.Revision, x.Last?.ActorObjectId, x.UpdatedAtUtc, x.Last?.Reason, false, null, null,
            ScopeValue: x.Value, Wave: x.Wave)).ToList(), await query.CountAsync(token));
    }

    private static async Task<PageResult<DecisionRegisterRow>> AssessmentsAsync(ClarityDbContext db, Project project,
        IQueryable<InventoryObject> objects, DecisionKind kind, bool toReviewOnly, PageRequest page, CancellationToken token)
    {
        var assessment = Assessment(kind);
        var query = from a in db.Assessments.AsNoTracking()
                    join i in objects on new { a.WorkspaceId, Id = a.ObjectId } equals new { i.WorkspaceId, i.Id }
                    where a.WorkspaceId == project.WorkspaceId && a.ProjectId == project.Id && a.Kind == assessment
                        && !db.Assessments.Any(b => b.WorkspaceId == a.WorkspaceId && b.ProjectId == a.ProjectId && b.ObjectId == a.ObjectId
                            && b.Kind == a.Kind && b.Revision > a.Revision)
                        && (!toReviewOnly || db.ReviewFlags.Any(f => f.WorkspaceId == a.WorkspaceId && f.ProjectId == a.ProjectId
                            && f.Kind == kind && f.ObjectId == a.ObjectId))
                    select new { a, i };
        var items = await query.OrderBy(x => x.i.DisplayName).ThenBy(x => x.i.Id).Skip(page.Start).Take(page.Count)
            .Select(x => new
            {
                x.i.Id,
                x.i.EnvironmentId,
                Label = x.i.DisplayName ?? x.i.UserPrincipalName,
                x.i.Type,
                x.i.Presence,
                x.a.Value,
                x.a.ScopeValue,
                x.a.Revision,
                x.a.AuthorObjectId,
                x.a.RecordedAtUtc,
                x.a.Reason
            }).ToListAsync(token);
        return new(items.Select(x => new DecisionRegisterRow(kind, x.Id, x.EnvironmentId, x.Label ?? x.Id.ToString(), x.Type, x.Presence,
            kind == DecisionKind.SsoDependency ? $"SsoDependency_{x.Value}" : $"QualificationScope_{x.ScopeValue ?? QualificationScopeValue.Unknown}",
            null, x.Revision, x.AuthorObjectId, x.RecordedAtUtc, x.Reason, false, null, null)).ToList(), await query.CountAsync(token));
    }

    private static async Task<PageResult<DecisionRegisterRow>> MappingAsync(ClarityDbContext db, Project project, IQueryable<InventoryObject> objects,
        bool toReviewOnly, PageRequest page, CancellationToken token)
    {
        var query = from m in db.MappingDecisions.AsNoTracking()
                    join a in objects on new { m.WorkspaceId, Id = m.AdObjectId } equals new { a.WorkspaceId, a.Id }
                    join b in db.InventoryObjects on new { m.WorkspaceId, Id = m.EntraObjectId } equals new { b.WorkspaceId, b.Id }
                    where m.WorkspaceId == project.WorkspaceId && m.ProjectId == project.Id && (!toReviewOnly || m.State == MappingState.ToReview)
                    select new { m, a, b };
        var items = await query.OrderBy(x => x.a.DisplayName).ThenBy(x => x.m.Id).Skip(page.Start).Take(page.Count)
            .Select(x => new
            {
                x.a.Id,
                x.a.EnvironmentId,
                Label = x.a.UserPrincipalName ?? x.a.DisplayName,
                x.a.Type,
                x.a.Presence,
                MappingId = x.m.Id,
                x.m.State,
                x.m.Revision,
                Counterpart = x.b.UserPrincipalName ?? x.b.DisplayName,
                Last = db.MappingRevisions.Where(r => r.WorkspaceId == x.m.WorkspaceId && r.DecisionId == x.m.Id && r.Number == x.m.Revision)
                    .Select(r => new { r.Reason, r.ActorObjectId, r.OccurredAtUtc }).FirstOrDefault()
            }).ToListAsync(token);
        return new(items.Select(x => new DecisionRegisterRow(DecisionKind.Mapping, x.Id, x.EnvironmentId, x.Label ?? x.Id.ToString(), x.Type, x.Presence,
            x.State.ToString(), x.Counterpart, x.Revision, x.Last?.ActorObjectId, x.Last?.OccurredAtUtc, x.Last?.Reason,
            x.State == MappingState.ToReview, null, null, x.MappingId)).ToList(), await query.CountAsync(token));
    }
}
