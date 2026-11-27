using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Core;

public enum RunChange { Added, Modified, Removed, NewlyObserved, NotObserved, NotCompared, Unchanged }
public enum UnitComparison { Comparable, NotExhaustive, DifferentStream, BaselineOnly, LaterOnly }
public enum DecisionKind { Scope, Mapping, SsoDependency, TeamsChatScope, TeamsTelephonyScope }

/// <summary>Fields whose change makes each kind of consultant decision due for review, shared by refreshes and run comparisons.</summary>
public static class DecisionReview
{
    public static readonly string[] MappingFields = ["userPrincipalName", "mail", "proxyAddresses", "objectSid", "msDSConsistencyGuid",
        "onPremisesImmutableId", "onPremisesSecurityIdentifier", "onPremisesSyncEnabled", "deviceId", "trustType", "serialNumber", "displayName",
        "enabled", "accountEnabled"];
    public static readonly string[] SsoFields = ["appId", "signInAudience", "servicePrincipalType", "accountEnabled", "appRoleAssignmentRequired",
        "preferredSingleSignOnMode"];
    public static readonly string[] SsoTypes = ["application", "servicePrincipal"];
    public static readonly string[] TeamsFields = ["visibility"];
    public static readonly string[] TeamsTypes = ["team"];
    private static readonly string[] MappingTypes = ["user", "device", "computer"];
    private static readonly string[] UserMappingFields = MappingFields.Where(f => f != "displayName").ToArray();

    public static bool Applies(DecisionKind kind, string type) => kind switch
    {
        DecisionKind.Scope => true,
        DecisionKind.Mapping => MappingTypes.Contains(type, StringComparer.Ordinal),
        DecisionKind.SsoDependency => SsoTypes.Contains(type, StringComparer.Ordinal),
        _ => TeamsTypes.Contains(type, StringComparer.Ordinal)
    };

    /// <summary>Critical fields of a decision kind; a user's display name never invalidates its hybrid mapping.</summary>
    public static IReadOnlyList<string> Fields(DecisionKind kind, string type) => kind switch
    {
        DecisionKind.Scope => ScopeRules.CriticalFields,
        DecisionKind.Mapping => type == "user" ? UserMappingFields : MappingFields,
        DecisionKind.SsoDependency => SsoFields,
        _ => TeamsFields
    };

    /// <summary>
    /// True when a change can invalidate a decision of that kind on that object: a proven removal, or a modification of one of its critical
    /// fields. Additions, unproven absences and uncompared objects never do, so a targeted refresh leaves valid decisions untouched.
    /// </summary>
    public static bool Affects(DecisionKind kind, string type, RunChange change, IReadOnlyCollection<string> changedFields) =>
        Applies(kind, type) && (change == RunChange.Removed
            || change == RunChange.Modified && Fields(kind, type).Any(f => changedFields.Contains(f, StringComparer.Ordinal)));
}

/// <summary>A replaceable comparison snapshot between two collections of one environment; it never alters decisions.</summary>
public sealed class RunComparison : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid BaselineRunId { get; init; }
    public Guid LaterRunId { get; init; }
    public Guid OperationId { get; init; }
    public Guid ActorTenantId { get; init; }
    public Guid ActorObjectId { get; init; }
    public DateTimeOffset ComputedAtUtc { get; init; }
    public int Added { get; init; }
    public int Modified { get; init; }
    public int Removed { get; init; }
    public int NewlyObserved { get; init; }
    public int NotObserved { get; init; }
    public int NotCompared { get; init; }
    public int Unchanged { get; init; }
    public int RelationsAdded { get; init; }
    public int RelationsRemoved { get; init; }
    public int RelationsUnknown { get; init; }
    public required string UnitsJson { get; init; }
}

public sealed class RunComparisonItem : WorkspaceEntity
{
    public Guid ComparisonId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid ObjectId { get; init; }
    public required string Type { get; init; }
    public string? DisplayName { get; init; }
    public RunChange Change { get; init; }
    public bool Tombstone { get; init; }
    public string ChangedFields { get; init; } = "";
    public string ChangesJson { get; init; } = "[]";
    public bool ScopeReview { get; init; }
    public bool MappingReview { get; init; }
    public bool AssessmentReview { get; init; }
}

public static class RunComparisonRules
{
    public const int MaxObservations = 200_000;
    public const int MaxRelations = 500_000;
    public const int MaxValueLength = 500;
    public const int MaxFields = 100;
}

/// <summary>A published section of one run. Exhaustive means it proves absence; Complete means its rows carry every covered field.</summary>
public sealed record ComparedSection(Guid Id, string CapabilityId, string ScopeHash, CollectionScope Scope, string Stream, bool Exhaustive, bool Complete,
    CoverageStatus Coverage, IReadOnlySet<string> CoveredFields);
public sealed record ComparedObject(Guid SectionId, Guid ObjectId, string Type, DateTimeOffset ObservedAtUtc,
    IReadOnlyDictionary<string, JsonElement> Attributes, bool Tombstone);
public sealed record ComparedRelation(Guid SectionId, string IdentityHash);
public sealed record ComparedRun(IReadOnlyList<ComparedSection> Sections, IReadOnlyList<ComparedObject> Objects, IReadOnlyList<ComparedRelation> Relations);

public sealed record FieldChange(string Field, string? Before, string? After);
public sealed record ComparedItem(Guid ObjectId, string Type, RunChange Change, bool Tombstone, IReadOnlyList<string> ChangedFields,
    IReadOnlyList<FieldChange> Changes, bool ScopeReview, bool MappingReview, bool AssessmentReview);
public sealed record ComparisonUnit(string CapabilityId, string ScopeKind, string ScopeValue, UnitComparison Comparison,
    CoverageStatus? BaselineCoverage, CoverageStatus? LaterCoverage, int BaselineObjects, int LaterObjects,
    int RelationsAdded, int RelationsRemoved, int RelationsUnknown);
public sealed record RunComparisonResult(IReadOnlyList<ComparisonUnit> Units, IReadOnlyList<ComparedItem> Items, IReadOnlyDictionary<RunChange, int> Counts)
{
    public int Count(RunChange change) => Counts.GetValueOrDefault(change);
}

/// <summary>
/// Compares two collections of one environment. Removal and addition are only asserted where both runs published an exhaustive section
/// of the same capability, scope and producer stream; anything else stays not observed or newly observed, never a fabricated deletion.
/// </summary>
public static class RunComparisonEngine
{
    private sealed record Unit(string CapabilityId, string ScopeHash, CollectionScope Scope, string? Stream, bool Exhaustive, bool Complete,
        CoverageStatus Coverage, HashSet<string> Fields, HashSet<Guid> Sections);

    public static RunComparisonResult Compare(ComparedRun baseline, ComparedRun later)
    {
        if (baseline.Objects.Count > RunComparisonRules.MaxObservations || later.Objects.Count > RunComparisonRules.MaxObservations
            || baseline.Relations.Count > RunComparisonRules.MaxRelations || later.Relations.Count > RunComparisonRules.MaxRelations)
        {
            throw new ArgumentException("The collections are too large for one comparison.");
        }
        var before = Units(baseline);
        var after = Units(later);
        var keys = before.Keys.Union(after.Keys).Order().ToList();
        var comparison = keys.ToDictionary(k => k, k => Classify(before.GetValueOrDefault(k), after.GetValueOrDefault(k)), StringComparer.Ordinal);
        var beforeUnit = before.Values.SelectMany(u => u.Sections.Select(s => (s, Key(u)))).ToDictionary(x => x.s, x => x.Item2);
        var afterUnit = after.Values.SelectMany(u => u.Sections.Select(s => (s, Key(u)))).ToDictionary(x => x.s, x => x.Item2);
        var oldObjects = baseline.Objects.Where(o => beforeUnit.ContainsKey(o.SectionId)).ToLookup(o => o.ObjectId);
        var newObjects = later.Objects.Where(o => afterUnit.ContainsKey(o.SectionId)).ToLookup(o => o.ObjectId);

        var items = new List<ComparedItem>();
        var counts = Enum.GetValues<RunChange>().ToDictionary(c => c, _ => 0);
        foreach (var id in oldObjects.Select(g => g.Key).Union(newObjects.Select(g => g.Key)).Order())
        {
            var old = oldObjects[id].ToList();
            var current = newObjects[id].ToList();
            var type = (current.Count != 0 ? current : old)[0].Type;
            var change = RunChange.Unchanged;
            var tombstone = false;
            IReadOnlyList<FieldChange> changes = [];
            IReadOnlyList<string> changed = [];
            if (old.Count == 0)
            {
                tombstone = current.Any(o => o.Tombstone);
                change = tombstone ? RunChange.Removed
                    : current.All(o => comparison[afterUnit[o.SectionId]] == UnitComparison.Comparable) ? RunChange.Added : RunChange.NewlyObserved;
            }
            else if (current.Count == 0)
            {
                change = old.All(o => comparison[beforeUnit[o.SectionId]] == UnitComparison.Comparable) ? RunChange.Removed : RunChange.NotObserved;
            }
            else if (current.Any(o => o.Tombstone) && !old.Any(o => o.Tombstone))
            {
                change = RunChange.Removed;
                tombstone = true;
            }
            else
            {
                (changes, changed, var compared) = Diff(old, before, beforeUnit, current, after, afterUnit);
                change = !compared ? RunChange.NotCompared : changed.Count != 0 ? RunChange.Modified : RunChange.Unchanged;
            }
            counts[change]++;
            if (change == RunChange.Unchanged) { continue; }
            var assessment = DecisionReview.Affects(DecisionKind.SsoDependency, type, change, changed)
                || DecisionReview.Affects(DecisionKind.TeamsChatScope, type, change, changed);
            items.Add(new ComparedItem(id, type, change, tombstone, changed, changes,
                DecisionReview.Affects(DecisionKind.Scope, type, change, changed), DecisionReview.Affects(DecisionKind.Mapping, type, change, changed),
                assessment));
        }

        var oldRelations = baseline.Relations.Where(r => beforeUnit.ContainsKey(r.SectionId)).ToLookup(r => beforeUnit[r.SectionId], r => r.IdentityHash);
        var newRelations = later.Relations.Where(r => afterUnit.ContainsKey(r.SectionId)).ToLookup(r => afterUnit[r.SectionId], r => r.IdentityHash);
        var units = keys.Select(key =>
        {
            var b = before.GetValueOrDefault(key);
            var a = after.GetValueOrDefault(key);
            var oldSet = oldRelations[key].ToHashSet(StringComparer.Ordinal);
            var newSet = newRelations[key].ToHashSet(StringComparer.Ordinal);
            var added = newSet.Count(h => !oldSet.Contains(h));
            var removed = oldSet.Count(h => !newSet.Contains(h));
            var exact = comparison[key] == UnitComparison.Comparable;
            var scope = (a ?? b)!.Scope;
            return new ComparisonUnit((a ?? b)!.CapabilityId, scope.Kind, scope.Value, comparison[key], b?.Coverage, a?.Coverage,
                b is null ? 0 : oldObjects.Count(g => g.Any(o => b.Sections.Contains(o.SectionId))),
                a is null ? 0 : newObjects.Count(g => g.Any(o => a.Sections.Contains(o.SectionId))),
                exact ? added : 0, exact ? removed : 0, exact ? 0 : added + removed);
        }).ToList();
        return new(units, items, counts);
    }

    private static string Key(Unit unit) => unit.CapabilityId + "|" + unit.ScopeHash;

    private static Dictionary<string, Unit> Units(ComparedRun run) => run.Sections.GroupBy(s => s.CapabilityId + "|" + s.ScopeHash, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g =>
        {
            var first = g.First();
            var streams = g.Select(s => s.Stream).Distinct(StringComparer.Ordinal).ToList();
            var coverage = g.Select(s => s.Coverage).FirstOrDefault(c => c != CoverageStatus.Complete, CoverageStatus.Complete);
            return new Unit(first.CapabilityId, first.ScopeHash, first.Scope, streams.Count == 1 ? streams[0] : null,
                streams.Count == 1 && g.All(s => s.Exhaustive), g.All(s => s.Complete), coverage,
                g.SelectMany(s => s.CoveredFields).ToHashSet(StringComparer.Ordinal), g.Select(s => s.Id).ToHashSet());
        }, StringComparer.Ordinal);

    private static UnitComparison Classify(Unit? before, Unit? after) => (before, after) switch
    {
        (null, _) => UnitComparison.LaterOnly,
        (_, null) => UnitComparison.BaselineOnly,
        _ when before.Stream is null || after.Stream is null || before.Stream != after.Stream => UnitComparison.DifferentStream,
        _ when before.Exhaustive && after.Exhaustive => UnitComparison.Comparable,
        _ => UnitComparison.NotExhaustive
    };

    /// <summary>
    /// Field differences per capability observed in both runs. A field present on one side only counts as changed when the other side's
    /// sections were complete and covered it; otherwise its absence is unknown, not a removed value.
    /// </summary>
    private static (IReadOnlyList<FieldChange>, IReadOnlyList<string>, bool) Diff(List<ComparedObject> old, Dictionary<string, Unit> before,
        Dictionary<Guid, string> beforeUnit, List<ComparedObject> current, Dictionary<string, Unit> after, Dictionary<Guid, string> afterUnit)
    {
        static Dictionary<string, (Dictionary<string, JsonElement> Values, bool Complete, HashSet<string> Fields)> Merge(List<ComparedObject> rows,
            Dictionary<string, Unit> units, Dictionary<Guid, string> unitOf) =>
            rows.GroupBy(o => units[unitOf[o.SectionId]].CapabilityId, StringComparer.Ordinal).ToDictionary(g => g.Key, g =>
            {
                var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var row in g.OrderBy(o => o.ObservedAtUtc))
                {
                    foreach (var (field, value) in row.Attributes) { values[field] = value; }
                }
                var sections = g.Select(o => units[unitOf[o.SectionId]]).Distinct().ToList();
                return (values, sections.All(u => u.Complete), sections.SelectMany(u => u.Fields).ToHashSet(StringComparer.Ordinal));
            }, StringComparer.Ordinal);

        var oldValues = Merge(old, before, beforeUnit);
        var newValues = Merge(current, after, afterUnit);
        var common = oldValues.Keys.Intersect(newValues.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var changes = new Dictionary<string, FieldChange>(StringComparer.Ordinal);
        foreach (var capability in common)
        {
            var (a, aComplete, aFields) = oldValues[capability];
            var (b, bComplete, bFields) = newValues[capability];
            foreach (var field in a.Keys.Union(b.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (changes.ContainsKey(field)) { continue; }
                var hasOld = a.TryGetValue(field, out var x);
                var hasNew = b.TryGetValue(field, out var y);
                var different = hasOld && hasNew ? !JsonElement.DeepEquals(x, y)
                    : hasOld ? bComplete && bFields.Contains(field) && x.ValueKind != JsonValueKind.Null
                    : aComplete && aFields.Contains(field) && y.ValueKind != JsonValueKind.Null;
                if (different) { changes[field] = new(field, hasOld ? Text(x) : null, hasNew ? Text(y) : null); }
            }
        }
        var names = changes.Keys.Order(StringComparer.Ordinal).ToList();
        return (names.Take(RunComparisonRules.MaxFields).Select(n => changes[n]).ToList(), names, common.Count != 0);
    }

    private static string Text(JsonElement value)
    {
        var raw = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
        return raw.Length > RunComparisonRules.MaxValueLength ? raw[..RunComparisonRules.MaxValueLength] + "…" : raw;
    }
}
