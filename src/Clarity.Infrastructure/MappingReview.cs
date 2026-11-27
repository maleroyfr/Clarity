using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed record MappingSelection(Guid Id, long Revision);
public sealed record MappingRow(Guid Id, long Revision, MappingState State, AssociationRule Rule, Guid AdId, Guid EntraId,
    string AdLabel, string EntraLabel, string Forest, string Tenant);
public sealed record CorrectionPreview(MappingSelection Original, Guid ReplacementId, string ReplacementLabel, string EvidenceStamp, MappingSelection? Existing);

public sealed class MappingReview(IDbContextFactory<ClarityDbContext> factory, IProjectAccess access, IWorkspaceContext scope)
{
    public Task<PageResult<MappingRow>> PageAsync(Guid projectId, EnvironmentRole side, string state, PageRequest page, CancellationToken token = default) =>
        PageAsync(projectId, MappingKind.Hybrid, side, state, page, token);

    /// <summary>Hybrid rows pair two environments of one side; source-target rows pair a source with a target environment.</summary>
    public async Task<PageResult<MappingRow>> PageAsync(Guid projectId, MappingKind kind, EnvironmentRole side, string state, PageRequest page,
        CancellationToken token = default)
    {
        page.Validate();
        if (!Enum.IsDefined(kind)) { throw new ArgumentException("Invalid mapping kind."); }
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Read, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var decisions = Decisions(db, project).Where(x => x.Kind == kind);
        var (first, second) = kind == MappingKind.Hybrid ? (side, side) : (EnvironmentRole.Source, EnvironmentRole.Target);
        decisions = state switch
        {
            "Queue" => decisions.Where(x => x.State == MappingState.Proposed || x.State == MappingState.ToReview),
            "All" => decisions,
            _ when Enum.TryParse<MappingState>(state, out var s) && Enum.IsDefined(s) => decisions.Where(x => x.State == s),
            _ => throw new ArgumentException("Invalid mapping state.")
        };
        var query = from m in decisions
                    join a in db.InventoryObjects on new { m.WorkspaceId, Id = m.AdObjectId } equals new { a.WorkspaceId, a.Id }
                    join b in db.InventoryObjects on new { m.WorkspaceId, Id = m.EntraObjectId } equals new { b.WorkspaceId, b.Id }
                    join ad in db.Environments on new { m.WorkspaceId, Id = m.AdEnvironmentId } equals new { ad.WorkspaceId, ad.Id }
                    join cloud in db.Environments on new { m.WorkspaceId, Id = m.EntraEnvironmentId } equals new { cloud.WorkspaceId, cloud.Id }
                    where ad.Role == first && cloud.Role == second
                    orderby a.DisplayName, m.Id
                    select new MappingRow(m.Id, m.Revision, m.State, m.Rule, a.Id, b.Id,
                        a.UserPrincipalName ?? a.DisplayName ?? a.Id.ToString(), b.UserPrincipalName ?? b.DisplayName ?? b.Id.ToString(), ad.ExternalId, cloud.ExternalId);
        return new(await query.Skip(page.Start).Take(page.Count).ToListAsync(token), await query.CountAsync(token));
    }

    public async Task<int> PreviewAsync(Guid projectId, IReadOnlyList<MappingSelection> selected, bool validate, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        await using var db = await factory.CreateDbContextAsync(token);
        return (await AffectedAsync(db, project, selected, validate, token)).Count;
    }

    public async Task ApplyAsync(Guid projectId, IReadOnlyList<MappingSelection> selected, bool validate, int expectedCount, string reason,
        CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        InventoryMatching.ValidateReason(reason);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var rows = await AffectedAsync(db, project, selected, validate, token);
        if (rows.Count != expectedCount) { throw new ConflictException(); }
        foreach (var row in rows)
        { InventoryMatching.Record(db, row, validate ? MappingState.Validated : MappingState.Rejected, reason.Trim(), scope.Identity); }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task<CorrectionPreview> PreviewCorrectionAsync(Guid projectId, MappingSelection original, Guid replacement, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        await using var db = await factory.CreateDbContextAsync(token);
        return (await CorrectionAsync(db, project, original, replacement, token)).Preview;
    }

    public async Task CorrectAsync(Guid projectId, CorrectionPreview preview, string reason, CancellationToken token = default)
    {
        var project = await access.RequireAsync(projectId, WorkspaceOperation.Qualify, token);
        InventoryMatching.ValidateReason(reason);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await InventoryMatching.LockProjectAsync(db, project, token);
        var (current, original, target) = await CorrectionAsync(db, project, preview.Original, preview.ReplacementId, token);
        if (current != preview) { throw new ConflictException(); }
        var replacement = target ?? new MappingDecision
        {
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            AdEnvironmentId = original.AdEnvironmentId,
            EntraEnvironmentId = original.EntraEnvironmentId,
            AdObjectId = original.AdObjectId,
            EntraObjectId = preview.ReplacementId,
            Fingerprint = "",
            EvidenceJson = """{"observations":[]}""",
            Rule = AssociationRule.ConsultantQualified
        };
        if (target == null) { db.Add(replacement); }
        replacement.ConsultantQualified = true;
        await db.SaveChangesAsync(token);
        await InventoryMatching.RefreshAsync(db, project, token);
        InventoryMatching.Record(db, original, MappingState.Rejected, reason.Trim(), scope.Identity);
        InventoryMatching.Record(db, replacement, MappingState.Validated, reason.Trim(), scope.Identity);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private static IQueryable<MappingDecision> Decisions(ClarityDbContext db, Project project) =>
        db.MappingDecisions.Where(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == project.Id);

    private static async Task<List<MappingDecision>> AffectedAsync(ClarityDbContext db, Project project, IReadOnlyList<MappingSelection> selection,
        bool validate, CancellationToken token)
    {
        if (selection.Count is < 1 or > 100 || selection.DistinctBy(r => r.Id).Count() != selection.Count)
        { throw new ArgumentException("Select 1 to 100 distinct associations."); }
        var revisions = selection.ToDictionary(r => r.Id, r => r.Revision);
        var ids = revisions.Keys.ToArray();
        var decisions = Decisions(db, project);
        var rows = await decisions.Where(x => ids.Contains(x.Id)).ToListAsync(token);
        if (rows.Count != ids.Length) { throw new AccessDeniedException(); }
        if (rows.Any(r => r.Revision != revisions[r.Id])) { throw new ConflictException(); }
        if (rows.Select(r => r.Kind).Distinct().Count() != 1) { throw new ArgumentException("Select associations of one kind."); }
        if (validate)
        {
            // A source object may have one validated counterpart per target environment; a hybrid object has one overall.
            var kind = rows[0].Kind;
            Guid Scope(MappingDecision d) => kind == MappingKind.Hybrid ? Guid.Empty : d.EntraEnvironmentId;
            var ad = rows.Select(x => x.AdObjectId).Distinct().ToArray();
            var cloud = rows.Select(x => x.EntraObjectId).Distinct().ToArray();
            var claimed = rows.Select(x => (x.AdObjectId, Scope(x))).ToHashSet();
            if (claimed.Count != rows.Count || cloud.Length != rows.Count || rows.Any(r => r.Rule == AssociationRule.NoCurrentCandidate))
            { throw new ConflictException(); }
            var others = await decisions.Where(x => x.Kind == kind && !ids.Contains(x.Id) && x.State == MappingState.Validated
                && (ad.Contains(x.AdObjectId) || cloud.Contains(x.EntraObjectId))).ToListAsync(token);
            if (others.Any(x => cloud.Contains(x.EntraObjectId) || claimed.Contains((x.AdObjectId, Scope(x))))) { throw new ConflictException(); }
        }
        return rows.Where(r => r.State != (validate ? MappingState.Validated : MappingState.Rejected)).ToList();
    }

    private static async Task<(CorrectionPreview Preview, MappingDecision Original, MappingDecision? Target)> CorrectionAsync(
        ClarityDbContext db, Project project, MappingSelection selection, Guid replacementId, CancellationToken token)
    {
        var decisions = Decisions(db, project);
        var original = await decisions.SingleOrDefaultAsync(x => x.Id == selection.Id, token) ?? throw new AccessDeniedException();
        if (original.Kind != MappingKind.Hybrid) { throw new ArgumentException("Source-target associations follow the project's mapping rules."); }
        if (original.Revision != selection.Revision || original.EntraObjectId == replacementId) { throw new ConflictException(); }
        var ad = await db.InventoryObjects.SingleAsync(x => x.WorkspaceId == project.WorkspaceId && x.EnvironmentId == original.AdEnvironmentId
            && x.Id == original.AdObjectId, token);
        var replacement = await db.InventoryObjects.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.EnvironmentId == original.EntraEnvironmentId && x.Id == replacementId, token) ?? throw new AccessDeniedException();
        if (ad.Presence != InventoryPresence.Observed || replacement.Presence != InventoryPresence.Observed
            || (ad.Type, replacement.Type) is not (("user", "user") or ("computer", "device"))) { throw new ConflictException(); }
        var target = await decisions.SingleOrDefaultAsync(x => x.AdObjectId == ad.Id && x.EntraObjectId == replacementId, token);
        if (await decisions.AnyAsync(x => x.Kind == MappingKind.Hybrid && x.Id != original.Id && x.Id != (target == null ? Guid.Empty : target.Id)
            && x.State == MappingState.Validated
            && (x.AdObjectId == ad.Id || x.EntraObjectId == replacementId), token)) { throw new ConflictException(); }
        var proofIds = await db.CurrentValues.Where(x => x.WorkspaceId == project.WorkspaceId && x.ObjectId == replacementId)
            .Select(x => x.ObservationId).Distinct().Order().ToListAsync(token);
        var stamp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(proofIds)));
        return (new(selection, replacement.Id, replacement.UserPrincipalName ?? replacement.DisplayName ?? replacement.Id.ToString(), stamp,
            target == null ? null : new(target.Id, target.Revision)), original, target);
    }
}
