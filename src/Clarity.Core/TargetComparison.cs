using System.Text.RegularExpressions;

namespace Clarity.Core;

/// <summary>
/// Hybrid mappings pair an AD object with its synchronized Entra object on one side. Source-target mappings pair a source object with
/// its target counterpart: the Ad* columns then hold the source side and the Entra* columns the target side.
/// </summary>
public enum MappingKind { Hybrid, SourceTarget }
public enum TargetRuleKind { SharedAttribute, ComputedUpn }
public enum TargetDataState { Planned, NoData, Available }
public enum TargetOutcome { Matched, Ambiguous, Absent, Unknown, NotCompared }
public enum CollisionKind { Upn, Smtp, Alias, Domain }

/// <summary>
/// A documented source-to-target correspondence rule of a project. Rules are never edited: a change retires the rule and adds another,
/// so every comparison keeps the exact rules it applied.
/// </summary>
public sealed class TargetMappingRule : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public required string ObjectType { get; init; }
    public TargetRuleKind Kind { get; init; }
    public required string SourceField { get; init; }
    public required string TargetField { get; init; }
    public string? TargetDomain { get; init; }
    public int Priority { get; init; }
    public required string Justification { get; init; }
    public Guid OperationId { get; init; }
    public Guid CreatedByTenantId { get; init; }
    public Guid CreatedByObjectId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public Guid? RetiredByTenantId { get; set; }
    public Guid? RetiredByObjectId { get; set; }
    public DateTimeOffset? RetiredAtUtc { get; set; }
    public string? RetirementReason { get; set; }
}

/// <summary>A replaceable source-target comparison snapshot of one environment pair, with the rules and coverage it used.</summary>
public sealed class TargetComparison : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid SourceEnvironmentId { get; init; }
    public Guid TargetEnvironmentId { get; init; }
    public TargetDataState Data { get; init; }
    public Guid OperationId { get; init; }
    public Guid ActorTenantId { get; init; }
    public Guid ActorObjectId { get; init; }
    public DateTimeOffset ComputedAtUtc { get; init; }
    public int Matched { get; init; }
    public int Ambiguous { get; init; }
    public int Absent { get; init; }
    public int Unknown { get; init; }
    public int NotCompared { get; init; }
    public int Collisions { get; init; }
    public required string RulesJson { get; init; }
    public required string CoverageJson { get; init; }
}

/// <summary>Either the outcome for one source object (Outcome set) or one collision (Collision set).</summary>
public sealed class TargetComparisonItem : WorkspaceEntity
{
    public Guid ComparisonId { get; init; }
    public Guid SourceEnvironmentId { get; init; }
    public Guid TargetEnvironmentId { get; init; }
    public Guid? SourceObjectId { get; init; }
    public Guid? TargetObjectId { get; init; }
    public Guid? OtherObjectId { get; init; }
    public TargetOutcome? Outcome { get; init; }
    public CollisionKind? Collision { get; init; }
    public required string ObjectType { get; init; }
    public string? Label { get; init; }
    public string? TargetLabel { get; init; }
    public string? OtherLabel { get; init; }
    public string Key { get; init; } = "";
    public int CandidateCount { get; init; }
    public required string Reason { get; init; }
    public Guid? RuleId { get; init; }
}

public static partial class TargetComparisonRules
{
    public const int MaxObjects = 200_000;
    public const int MaxRules = 50;
    public const int MaxKey = 500;
    public static readonly string[] ObjectTypes = ["user", "group", "mailbox", "sharedMailbox", "resourceMailbox", "mailGroup", "recipient", "device"];
    public static readonly string[] AddressTypes = ["user", "group", "mailbox", "sharedMailbox", "resourceMailbox", "mailGroup", "recipient"];
    public static readonly string[] DomainTypes = ["domain", "acceptedDomain"];
    public static readonly string[] CollisionFields = ["userPrincipalName", "mail", "proxyAddresses", "alias", "isVerified"];
    /// <summary>Every reason the engine can attach to an outcome or a collision.</summary>
    public static readonly string[] Reasons = ["SingleCandidate", "MultipleCandidates", "SharedCandidate", "NoCandidate", "TargetNotExhaustive",
        "SourceValueMissing", "SourceAttributeNotCollected", "TargetAttributeNotCollected", "TargetValueConflicting", "SourceValueConflicting",
        "TargetPlanned", "TargetDataMissing", "ComputedUpnShared", "TargetUpnTaken", "SmtpTaken", "AliasTaken", "SharedDomain", "TargetDomainNotVerified"];

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.]{0,99}$")]
    private static partial Regex FieldName();

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex DomainName();

    public static bool IsField(string? value) => value is not null && FieldName().IsMatch(value);
    public static bool IsDomain(string? value) => value is not null && DomainName().IsMatch(value);

    public static void Validate(TargetMappingRule rule)
    {
        if (!ObjectTypes.Contains(rule.ObjectType, StringComparer.Ordinal) || !Enum.IsDefined(rule.Kind) || !IsField(rule.SourceField)
            || !IsField(rule.TargetField) || rule.Priority is < 1 or > 100 || string.IsNullOrWhiteSpace(rule.Justification)
            || rule.Justification.Length > 1000
            || (rule.Kind == TargetRuleKind.ComputedUpn ? rule.TargetField != "userPrincipalName" || !IsDomain(rule.TargetDomain) : rule.TargetDomain is not null)
            || rule.RetiredAtUtc.HasValue != rule.RetirementReason is not null
            || rule.RetirementReason is { } reason && (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000))
        {
            throw new ArgumentException("Invalid target mapping rule.");
        }
    }

    public static void Validate(TargetComparisonItem item)
    {
        if (item.Outcome.HasValue == item.Collision.HasValue || item.Outcome is { } outcome && (!Enum.IsDefined(outcome) || item.SourceObjectId is null)
            || item.Collision is { } collision && !Enum.IsDefined(collision) || item.ObjectType.Length is < 1 or > 100 || item.Key.Length > MaxKey
            || !Reasons.Contains(item.Reason, StringComparer.Ordinal) || item.CandidateCount < 0)
        {
            throw new ArgumentException("Invalid target comparison item.");
        }
    }
}

/// <summary>An observed object with its current values per field; Owner is the canonical principal, such as the user of a mailbox.</summary>
public sealed record TargetCandidate(Guid Id, string Type, string Label, IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    IReadOnlySet<string> Conflicting, Guid? Owner = null)
{
    public Guid Principal => Owner ?? Id;
    public IReadOnlyList<string> this[string field] => Values.TryGetValue(field, out var values) ? values : [];
}
/// <summary>Fields collected for an object type and whether a complete tenant-wide section of that type proves absence.</summary>
public sealed record TargetCoverage(IReadOnlySet<string> Fields, bool Exhaustive);
public sealed record ObservedDomain(Guid ObjectId, string Name, bool Verified);
public sealed record TargetSideData(IReadOnlyList<TargetCandidate> Objects, IReadOnlyDictionary<string, TargetCoverage> Coverage,
    IReadOnlyList<ObservedDomain> Domains, bool HasDomainData);
public sealed record TargetRuleInput(Guid Id, string ObjectType, TargetRuleKind Kind, string SourceField, string TargetField, string? TargetDomain,
    int Priority);
public sealed record TargetMatch(Guid TargetId, Guid RuleId, string Key);
public sealed record TargetOutcomeItem(Guid SourceId, string Type, TargetOutcome Outcome, string Key, Guid? RuleId, string Reason,
    IReadOnlyList<TargetMatch> Matches, IReadOnlyList<Guid> Sharers);
public sealed record TargetCollisionItem(CollisionKind Kind, string Type, string Key, Guid? SourceId, Guid? TargetId, Guid? OtherSourceId,
    Guid? RuleId, string Reason);
public sealed record TargetComparisonResult(IReadOnlyList<TargetOutcomeItem> Outcomes, IReadOnlyList<TargetCollisionItem> Collisions)
{
    public int Count(TargetOutcome outcome) => Outcomes.Count(o => o.Outcome == outcome);
}

/// <summary>
/// Applies the project's documented rules to source objects. Absence is asserted only when every rule produced or legitimately lacked a key,
/// the target field was collected without conflict, and a complete tenant-wide section of the type was observed; otherwise the object is
/// unknown. A planned or uncollected target is never compared, but computed UPNs are still checked against each other.
/// </summary>
public static class TargetComparisonEngine
{
    private static readonly StringComparer Ignore = StringComparer.OrdinalIgnoreCase;

    public static TargetComparisonResult Compare(IReadOnlyList<TargetRuleInput> rules, TargetSideData source, TargetSideData target,
        TargetDataState data)
    {
        if (source.Objects.Count > TargetComparisonRules.MaxObjects || target.Objects.Count > TargetComparisonRules.MaxObjects)
        {
            throw new ArgumentException("The environments are too large for one comparison.");
        }
        var available = data == TargetDataState.Available;
        var byType = rules.OrderBy(r => r.Priority).ThenBy(r => r.Id).ToLookup(r => r.ObjectType, StringComparer.Ordinal);
        var indexes = new Dictionary<(string, string), ILookup<string, TargetCandidate>>();
        ILookup<string, TargetCandidate> Index(string type, string field)
        {
            if (!indexes.TryGetValue((type, field), out var index))
            {
                index = target.Objects.Where(o => o.Type == type).SelectMany(o => o[field].Select(v => (Value: v.Trim(), o)))
                    .Where(x => x.Value.Length != 0).Distinct().ToLookup(x => x.Value, x => x.o, Ignore);
                indexes[(type, field)] = index;
            }
            return index;
        }
        var conflicted = target.Objects.SelectMany(o => o.Conflicting.Select(f => (o.Type, f))).ToHashSet();

        var computed = new List<(TargetCandidate Source, TargetRuleInput Rule, string Key)>();
        var evaluated = new List<(TargetCandidate Source, List<TargetMatch> Matches, string? Key, Guid? Rule, string? Blocker, string? Missing)>();
        foreach (var item in source.Objects.Where(o => byType.Contains(o.Type)).OrderBy(o => o.Label, StringComparer.Ordinal).ThenBy(o => o.Id))
        {
            var matches = new List<TargetMatch>();
            string? key = null, blocker = null, missing = null;
            Guid? credited = null;
            foreach (var rule in byType[item.Type])
            {
                if (item.Conflicting.Contains(rule.SourceField))
                {
                    blocker ??= "SourceValueConflicting";
                    continue;
                }
                var values = item[rule.SourceField].Select(v => v.Trim()).Where(v => v.Length != 0).ToList();
                if (values.Count == 0)
                {
                    if (source.Coverage.GetValueOrDefault(item.Type)?.Fields.Contains(rule.SourceField) == true) { missing ??= "SourceValueMissing"; }
                    else { blocker ??= "SourceAttributeNotCollected"; }
                    continue;
                }
                var keys = (rule.Kind == TargetRuleKind.SharedAttribute ? values : values.Select(v => ComputedUpn(v, rule.TargetDomain!)).OfType<string>())
                    .Where(k => k.Length <= TargetComparisonRules.MaxKey).Distinct(Ignore).ToList();
                if (keys.Count == 0)
                {
                    missing ??= "SourceValueMissing";
                    continue;
                }
                key ??= keys[0];
                credited ??= rule.Id;
                if (rule.Kind == TargetRuleKind.ComputedUpn) { computed.AddRange(keys.Select(k => (item, rule, k))); }
                if (!available) { continue; }
                var coverage = target.Coverage.GetValueOrDefault(item.Type);
                if (coverage is null || !coverage.Fields.Contains(rule.TargetField)) { blocker ??= "TargetAttributeNotCollected"; }
                else if (conflicted.Contains((item.Type, rule.TargetField))) { blocker ??= "TargetValueConflicting"; }
                foreach (var value in keys)
                {
                    foreach (var candidate in Index(item.Type, rule.TargetField)[value])
                    {
                        if (matches.All(m => m.TargetId != candidate.Id)) { matches.Add(new(candidate.Id, rule.Id, value)); }
                    }
                }
            }
            evaluated.Add((item, matches, key, credited, blocker, missing));
        }

        var sharers = evaluated.SelectMany(e => e.Matches.Select(m => (m.TargetId, e.Source.Id))).ToLookup(x => x.TargetId, x => x.Item2);
        var outcomes = new List<TargetOutcomeItem>();
        foreach (var (item, matches, key, rule, blocker, missing) in evaluated)
        {
            var competing = matches.SelectMany(m => sharers[m.TargetId]).Where(id => id != item.Id).Distinct().Order().ToList();
            var coverage = target.Coverage.GetValueOrDefault(item.Type);
            var (outcome, reason) = !available ? (TargetOutcome.NotCompared, data == TargetDataState.Planned ? "TargetPlanned" : "TargetDataMissing")
                : matches.Count > 1 ? (TargetOutcome.Ambiguous, "MultipleCandidates")
                : matches.Count == 1 ? competing.Count != 0 ? (TargetOutcome.Ambiguous, "SharedCandidate") : (TargetOutcome.Matched, "SingleCandidate")
                : key is null ? (TargetOutcome.Unknown, blocker ?? missing ?? "SourceValueMissing")
                : blocker is not null ? (TargetOutcome.Unknown, blocker)
                : coverage?.Exhaustive != true ? (TargetOutcome.Unknown, "TargetNotExhaustive")
                : (TargetOutcome.Absent, "NoCandidate");
            outcomes.Add(new(item.Id, item.Type, outcome, matches.FirstOrDefault()?.Key ?? key ?? "", matches.FirstOrDefault()?.RuleId ?? rule, reason,
                matches, competing));
        }

        var collisions = new List<TargetCollisionItem>();
        foreach (var group in computed.GroupBy(c => c.Key, Ignore).Where(g => g.Select(c => c.Source.Id).Distinct().Count() > 1))
        {
            foreach (var entry in group.DistinctBy(c => c.Source.Id))
            {
                collisions.Add(new(CollisionKind.Upn, entry.Source.Type, entry.Key, entry.Source.Id, null,
                    group.First(c => c.Source.Id != entry.Source.Id).Source.Id, entry.Rule.Id, "ComputedUpnShared"));
            }
        }
        if (available)
        {
            var shared = sharers.Where(g => g.Distinct().Count() > 1).Select(g => g.Key).ToHashSet();
            var sourceById = source.Objects.DistinctBy(o => o.Id).ToDictionary(o => o.Id);
            var targetById = target.Objects.DistinctBy(o => o.Id).ToDictionary(o => o.Id);
            var mapped = new Dictionary<Guid, HashSet<Guid>>();
            foreach (var outcome in outcomes)
            {
                var principal = sourceById[outcome.SourceId].Principal;
                if (!mapped.TryGetValue(principal, out var targets)) { mapped[principal] = targets = []; }
                foreach (var match in outcome.Matches)
                {
                    targets.Add(match.TargetId);
                    if (targetById.TryGetValue(match.TargetId, out var found)) { targets.Add(found.Principal); }
                }
            }
            bool Foreign(TargetCandidate item, TargetCandidate owner) =>
                mapped.GetValueOrDefault(item.Principal) is not { } targets || !targets.Contains(owner.Id) && !targets.Contains(owner.Principal);
            var seen = new HashSet<(CollisionKind, Guid, string, Guid)>();
            void Add(CollisionKind kind, TargetCandidate item, string key, TargetCandidate owner, Guid? rule, string reason)
            {
                if (seen.Add((kind, item.Principal, key.ToUpperInvariant(), owner.Principal)))
                {
                    collisions.Add(new(kind, item.Type, key, item.Id, owner.Id, null, rule, reason));
                }
            }
            var upns = Values(target.Objects, "userPrincipalName");
            foreach (var (item, rule, key) in computed.DistinctBy(c => (c.Source.Id, c.Key.ToUpperInvariant())))
            {
                foreach (var owner in upns[key].Where(t => Foreign(item, t) || shared.Contains(t.Id)))
                {
                    Add(CollisionKind.Upn, item, key, owner, rule.Id, "TargetUpnTaken");
                }
            }
            var sources = source.Objects.Where(o => TargetComparisonRules.AddressTypes.Contains(o.Type, StringComparer.Ordinal)).ToList();
            void Taken(CollisionKind kind, string reason, Func<TargetCandidate, IEnumerable<string>> keys)
            {
                var owners = target.Objects.Where(o => TargetComparisonRules.AddressTypes.Contains(o.Type, StringComparer.Ordinal))
                    .SelectMany(o => keys(o).Select(k => (k, o))).Distinct().ToLookup(x => x.k, x => x.o, Ignore);
                foreach (var item in sources)
                {
                    foreach (var key in keys(item).Distinct(Ignore))
                    {
                        foreach (var owner in owners[key].Where(o => Foreign(item, o))) { Add(kind, item, key, owner, null, reason); }
                    }
                }
            }
            Taken(CollisionKind.Smtp, "SmtpTaken", Addresses);
            Taken(CollisionKind.Alias, "AliasTaken", o => o["alias"].Select(a => a.Trim()).Where(a => a.Length != 0));
        }
        var targetDomains = target.Domains.ToLookup(d => d.Name, Ignore);
        foreach (var domain in source.Domains.DistinctBy(d => d.Name, Ignore))
        {
            if (targetDomains[domain.Name].FirstOrDefault() is { } other)
            {
                collisions.Add(new(CollisionKind.Domain, "domain", domain.Name.ToLowerInvariant(), domain.ObjectId, other.ObjectId, null, null, "SharedDomain"));
            }
        }
        if (target.HasDomainData)
        {
            var verified = target.Domains.Where(d => d.Verified).Select(d => d.Name).ToHashSet(Ignore);
            foreach (var rule in rules.Where(r => r.Kind == TargetRuleKind.ComputedUpn && !verified.Contains(r.TargetDomain!))
                .OrderBy(r => r.Priority).ThenBy(r => r.Id))
            {
                collisions.Add(new(CollisionKind.Domain, rule.ObjectType, rule.TargetDomain!, null, null, null, rule.Id, "TargetDomainNotVerified"));
            }
        }
        return new(outcomes, collisions.Where(c => c.Key.Length <= TargetComparisonRules.MaxKey).Distinct().ToList());
    }

    /// <summary>The local part of a source value before any '@', joined to the rule's target domain.</summary>
    public static string? ComputedUpn(string value, string domain)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        var local = (at >= 0 ? value[..at] : value).Trim();
        return local.Length == 0 || local.Any(char.IsWhiteSpace) ? null : $"{local}@{domain}".ToLowerInvariant();
    }

    /// <summary>The primary mail and every SMTP proxy address; X500, SIP and other address types never become keys.</summary>
    public static IEnumerable<string> Addresses(TargetCandidate item)
    {
        foreach (var mail in item["mail"]) { if (mail.Trim() is { Length: > 0 } value) { yield return value; } }
        foreach (var proxy in item["proxyAddresses"])
        {
            if (proxy.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase) && proxy[5..].Trim() is { Length: > 0 } value) { yield return value; }
        }
    }

    private static ILookup<string, TargetCandidate> Values(IEnumerable<TargetCandidate> objects, string field) =>
        objects.SelectMany(o => o[field].Select(v => (Value: v.Trim(), o))).Where(x => x.Value.Length != 0).Distinct()
            .ToLookup(x => x.Value, x => x.o, Ignore);
}
