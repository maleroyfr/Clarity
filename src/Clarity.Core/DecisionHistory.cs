using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clarity.Core;

/// <summary>One current critical value an independent decision was based on.</summary>
public sealed record BasisValue(string Field, string ValueJson, bool Conflicting);

/// <summary>A critical field whose value differs between the recorded basis and the current inventory; null means absent.</summary>
public sealed record BasisChange(string Field, string? Before, string? After);

/// <summary>
/// The exact critical facts a scope decision or an assessment was recorded against. Its SHA-256 is the decision fingerprint, so a stored
/// basis both proves the fingerprint and explains which fields changed when a refresh makes the decision due for review.
/// </summary>
public static class DecisionBasis
{
    public const string PresenceField = "presence";

    public static string Serialize(InventoryPresence presence, IEnumerable<BasisValue> values)
    {
        var fields = values.OrderBy(v => v.Field, StringComparer.Ordinal).ThenBy(v => v.ValueJson, StringComparer.Ordinal)
            .Select(v => new { v.Field, v.ValueJson, v.Conflicting }).ToList();
        return JsonSerializer.Serialize(new { Presence = presence, fields });
    }

    public static string Fingerprint(string basis) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(basis)));

    /// <summary>Changed critical fields, or null when the decision predates recorded bases and the change cannot be explained.</summary>
    public static IReadOnlyList<BasisChange>? Changes(string? recorded, string current)
    {
        if (recorded is null) { return null; }
        var (beforePresence, before) = Parse(recorded);
        var (afterPresence, after) = Parse(current);
        var changes = new List<BasisChange>();
        if (beforePresence != afterPresence)
        { changes.Add(new(PresenceField, ((InventoryPresence)beforePresence).ToString(), ((InventoryPresence)afterPresence).ToString())); }
        foreach (var field in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var old = before.GetValueOrDefault(field);
            var now = after.GetValueOrDefault(field);
            if (old != now) { changes.Add(new(field, old, now)); }
        }
        return changes;
    }

    private static (int Presence, Dictionary<string, string> Fields) Parse(string basis)
    {
        using var document = JsonDocument.Parse(basis);
        var root = document.RootElement;
        var fields = root.GetProperty("fields").EnumerateArray()
            .Select(f => (Field: f.GetProperty("Field").GetString()!, Value: f.GetProperty("ValueJson").GetString()!,
                Conflicting: f.GetProperty("Conflicting").GetBoolean()))
            .GroupBy(f => f.Field, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => string.Join(" | ", g.Select(f => f.Conflicting ? "⚠ " + f.Value : f.Value)), StringComparer.Ordinal);
        return (root.GetProperty("Presence").GetInt32(), fields);
    }
}

/// <summary>
/// Materialized re-review condition of one current scope decision or assessment: the inventory facts it was based on changed since it was
/// recorded. Refreshes only add, update or clear flags; they never alter the decision or its history.
/// </summary>
public sealed class ReviewFlag : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid ObjectId { get; init; }
    public DecisionKind Kind { get; init; }
    public long DecisionRevision { get; set; }
    public required string Fingerprint { get; set; }
    public string? ChangesJson { get; set; }
    public DateTimeOffset FlaggedAtUtc { get; set; }
}
