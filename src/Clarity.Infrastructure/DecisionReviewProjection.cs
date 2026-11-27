using System.Text.Json;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

/// <summary>Current presence and critical values of one inventory object, from which each decision kind derives its basis.</summary>
internal sealed record DecisionFacts(InventoryPresence Presence, IReadOnlyList<BasisValue> Values)
{
    public string Basis(IReadOnlyList<string> fields) =>
        DecisionBasis.Serialize(Presence, Values.Where(v => fields.Contains(v.Field, StringComparer.Ordinal)));
}

/// <summary>
/// Maintains <see cref="ReviewFlag"/> rows for the scope decisions and assessments of a set of objects. Called after each projection of
/// current values, it flags only decisions whose recorded critical facts no longer match, keeps the decisions and their histories untouched,
/// and clears flags once the facts match again or a consultant records a new revision.
/// </summary>
internal static class DecisionReviewProjection
{
    private static readonly string[] Fields = [.. ScopeRules.CriticalFields.Union(DecisionReview.SsoFields).Union(DecisionReview.TeamsFields)];
    private static readonly DecisionKind[] Flagged = [DecisionKind.Scope, DecisionKind.SsoDependency, DecisionKind.TeamsChatScope,
        DecisionKind.TeamsTelephonyScope];

    private sealed record Recorded(DecisionKind Kind, Guid ObjectId, Guid EnvironmentId, long Revision, string Fingerprint, string? BasisJson);

    public static DecisionKind Kind(AssessmentKind kind) => kind switch
    {
        AssessmentKind.SsoDependency => DecisionKind.SsoDependency,
        AssessmentKind.TeamsChatScope => DecisionKind.TeamsChatScope,
        _ => DecisionKind.TeamsTelephonyScope
    };

    public static IReadOnlyList<string> CriticalFields(DecisionKind kind) => DecisionReview.Fields(kind, "");

    public static async Task<Dictionary<Guid, DecisionFacts>> FactsAsync(ClarityDbContext db, Guid workspace, IEnumerable<Guid> ids,
        CancellationToken token)
    {
        var result = new Dictionary<Guid, DecisionFacts>();
        foreach (var chunk in ids.Distinct().Chunk(500))
        {
            var items = await db.InventoryObjects.AsNoTracking().Where(i => i.WorkspaceId == workspace && chunk.Contains(i.Id))
                .Select(i => new { i.Id, i.Presence }).ToListAsync(token);
            var values = (await db.CurrentValues.AsNoTracking().Where(v => v.WorkspaceId == workspace && chunk.Contains(v.ObjectId) && Fields.Contains(v.Field))
                .Select(v => new { v.ObjectId, v.Field, v.ValueJson, v.Conflicting }).ToListAsync(token)).ToLookup(v => v.ObjectId);
            foreach (var item in items)
            { result[item.Id] = new(item.Presence, values[item.Id].Select(v => new BasisValue(v.Field, v.ValueJson, v.Conflicting)).ToList()); }
        }
        return result;
    }

    /// <summary>Recomputes review flags of every recorded scope decision and latest assessment on these objects; returns the flagged count.</summary>
    public static async Task<int> RefreshAsync(ClarityDbContext db, Guid workspace, Guid projectId, IEnumerable<Guid> objectIds, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var flagged = 0;
        foreach (var chunk in objectIds.Distinct().Chunk(500))
        {
            var recorded = await db.ScopeDecisions.AsNoTracking()
                .Where(d => d.WorkspaceId == workspace && d.ProjectId == projectId && chunk.Contains(d.ObjectId))
                .Select(d => new Recorded(DecisionKind.Scope, d.ObjectId, d.EnvironmentId, d.Revision, d.Fingerprint, d.BasisJson)).ToListAsync(token);
            var assessments = await db.Assessments.AsNoTracking()
                .Where(a => a.WorkspaceId == workspace && a.ProjectId == projectId && chunk.Contains(a.ObjectId)
                    && !db.Assessments.Any(b => b.WorkspaceId == a.WorkspaceId && b.ProjectId == a.ProjectId && b.ObjectId == a.ObjectId
                        && b.Kind == a.Kind && b.Revision > a.Revision))
                .Select(a => new { a.Kind, a.ObjectId, a.EnvironmentId, a.Revision, a.MetadataFingerprint, a.BasisJson }).ToListAsync(token);
            recorded.AddRange(assessments.Select(a => new Recorded(Kind(a.Kind), a.ObjectId, a.EnvironmentId, a.Revision, a.MetadataFingerprint, a.BasisJson)));
            var flags = await db.ReviewFlags.Where(f => f.WorkspaceId == workspace && f.ProjectId == projectId && chunk.Contains(f.ObjectId)
                && Flagged.Contains(f.Kind)).ToListAsync(token);
            if (recorded.Count == 0 && flags.Count == 0) { continue; }
            var facts = await FactsAsync(db, workspace, recorded.Select(r => r.ObjectId), token);
            var existing = flags.ToDictionary(f => (f.Kind, f.ObjectId));
            var kept = new HashSet<(DecisionKind, Guid)>();
            foreach (var decision in recorded)
            {
                if (!facts.TryGetValue(decision.ObjectId, out var fact)) { continue; }
                var basis = fact.Basis(CriticalFields(decision.Kind));
                var fingerprint = DecisionBasis.Fingerprint(basis);
                if (fingerprint == decision.Fingerprint) { continue; }
                kept.Add((decision.Kind, decision.ObjectId));
                flagged++;
                var changes = DecisionBasis.Changes(decision.BasisJson, basis);
                var json = changes is null ? null : JsonSerializer.Serialize(changes);
                if (!existing.TryGetValue((decision.Kind, decision.ObjectId), out var flag))
                {
                    db.ReviewFlags.Add(new ReviewFlag
                    {
                        WorkspaceId = workspace,
                        ProjectId = projectId,
                        EnvironmentId = decision.EnvironmentId,
                        ObjectId = decision.ObjectId,
                        Kind = decision.Kind,
                        DecisionRevision = decision.Revision,
                        Fingerprint = fingerprint,
                        ChangesJson = json,
                        FlaggedAtUtc = now
                    });
                    continue;
                }
                if (flag.DecisionRevision != decision.Revision) { flag.FlaggedAtUtc = now; }
                flag.DecisionRevision = decision.Revision;
                flag.Fingerprint = fingerprint;
                flag.ChangesJson = json;
            }
            db.ReviewFlags.RemoveRange(flags.Where(f => !kept.Contains((f.Kind, f.ObjectId))));
        }
        return flagged;
    }

    public static IReadOnlyList<BasisChange>? ReadChanges(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<List<BasisChange>>(json);
}
