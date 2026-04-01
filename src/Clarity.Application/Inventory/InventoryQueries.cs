using Clarity.Application.Common;
using Clarity.Application.Common.Exceptions;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Inventory;

// ─── List snapshots with inventory summary ────────────────────────────────────

public sealed record ListSnapshotsWithInventorySummaryQuery(Guid? CustomerId = null)
    : IQuery<IReadOnlyList<SnapshotInventorySummaryDto>>;

public sealed class ListSnapshotsWithInventorySummaryHandler(
    ISnapshotRepository snapshotRepo,
    IInventoryObjectRepository inventoryRepo)
    : IQueryHandler<ListSnapshotsWithInventorySummaryQuery, IReadOnlyList<SnapshotInventorySummaryDto>>
{
    public async Task<IReadOnlyList<SnapshotInventorySummaryDto>> Handle(
        ListSnapshotsWithInventorySummaryQuery query, CancellationToken ct)
    {
        var snapshots = query.CustomerId.HasValue
            ? await snapshotRepo.GetByCustomerAsync(query.CustomerId.Value, ct)
            : await snapshotRepo.GetAllAsync(ct);

        var result = new List<SnapshotInventorySummaryDto>(snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            var typeSummaries = new List<InventoryTypeSummaryDto>();
            var totalObjects = 0;

            foreach (var objectType in Enum.GetValues<InventoryObjectType>())
            {
                var count = await inventoryRepo.CountBySnapshotAsync(snapshot.Id, objectType, ct);
                if (count == 0) continue;

                totalObjects += count;
                typeSummaries.Add(new InventoryTypeSummaryDto(
                    objectType,
                    InventoryTypeCategories.GetCategory(objectType),
                    count));
            }

            result.Add(new SnapshotInventorySummaryDto(
                snapshot.Id,
                snapshot.Name,
                snapshot.EnvironmentId,
                snapshot.CustomerId,
                totalObjects,
                typeSummaries));
        }

        return result;
    }
}

// ─── List inventory objects ───────────────────────────────────────────────────

public sealed record ListInventoryObjectsQuery(Guid SnapshotId, InventoryObjectType? Type = null)
    : IQuery<IReadOnlyList<InventoryObjectDto>>;

public sealed class ListInventoryObjectsHandler(IInventoryObjectRepository repo)
    : IQueryHandler<ListInventoryObjectsQuery, IReadOnlyList<InventoryObjectDto>>
{
    public async Task<IReadOnlyList<InventoryObjectDto>> Handle(
        ListInventoryObjectsQuery query, CancellationToken ct)
    {
        var objects = await repo.GetBySnapshotAsync(query.SnapshotId, query.Type, ct);
        return objects.Select(o => o.ToDto()).ToList();
    }
}

// ─── Get single inventory object ──────────────────────────────────────────────

public sealed record GetInventoryObjectByIdQuery(Guid SnapshotId, Guid ObjectId)
    : IQuery<InventoryObjectDto>;

public sealed class GetInventoryObjectByIdHandler(IInventoryObjectRepository repo)
    : IQueryHandler<GetInventoryObjectByIdQuery, InventoryObjectDto>
{
    public async Task<InventoryObjectDto> Handle(
        GetInventoryObjectByIdQuery query, CancellationToken ct)
    {
        var objects = await repo.GetBySnapshotAsync(query.SnapshotId, ct: ct);

        var obj = objects.FirstOrDefault(o => o.Id == query.ObjectId)
            ?? throw new NotFoundException(nameof(InventoryObject), query.ObjectId);

        return obj.ToDto();
    }
}

// ─── Mapper ───────────────────────────────────────────────────────────────────

internal static class InventoryObjectMappings
{
    internal static InventoryObjectDto ToDto(this InventoryObject o) => new(
        o.Id,
        o.SnapshotId,
        o.CollectorRunId,
        o.ObjectType,
        o.ExternalId,
        o.DisplayName,
        o.Properties);
}
