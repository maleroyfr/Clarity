using Clarity.Domain.Common;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Snapshots;

/// <summary>
/// Represents a single collected object (user, group, device, etc.) within a snapshot.
/// Properties are stored in a key/value bag for maximum schema flexibility.
/// </summary>
public sealed class InventoryObject : Entity
{
    private readonly Dictionary<string, string?> _properties = [];

    public Guid CollectorRunId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public InventoryObjectType ObjectType { get; private set; }

    /// <summary>Immutable ID from the source system (Graph object ID, AD SID, etc.).</summary>
    public string ExternalId { get; private set; } = default!;

    public string? DisplayName { get; private set; }
    public IReadOnlyDictionary<string, string?> Properties => _properties;

    /// <summary>Optional raw JSON payload from the source API/command for evidence retention.</summary>
    public string? RawDataJson { get; private set; }

    private InventoryObject() { } // EF Core

    public static InventoryObject Create(
        Guid snapshotId,
        Guid collectorRunId,
        InventoryObjectType objectType,
        string externalId,
        string? displayName,
        Dictionary<string, string?> properties,
        string? rawDataJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        var obj = new InventoryObject
        {
            SnapshotId = snapshotId,
            CollectorRunId = collectorRunId,
            ObjectType = objectType,
            ExternalId = externalId,
            DisplayName = displayName,
            RawDataJson = rawDataJson
        };

        foreach (var kv in properties)
            obj._properties[kv.Key] = kv.Value;

        return obj;
    }

    public string? GetProperty(string key) =>
        _properties.TryGetValue(key, out var v) ? v : null;
}
