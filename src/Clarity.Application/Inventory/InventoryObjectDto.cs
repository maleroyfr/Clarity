using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Inventory;

public sealed record InventoryObjectDto(
    Guid Id,
    Guid SnapshotId,
    Guid CollectorRunId,
    InventoryObjectType ObjectType,
    string ExternalId,
    string? DisplayName,
    IReadOnlyDictionary<string, string?> Properties);

public sealed record InventoryTypeSummaryDto(
    InventoryObjectType ObjectType,
    string Category,
    int Count);

public sealed record SnapshotInventorySummaryDto(
    Guid SnapshotId,
    string SnapshotName,
    Guid EnvironmentId,
    Guid CustomerId,
    int TotalObjects,
    IReadOnlyList<InventoryTypeSummaryDto> TypeSummaries);
