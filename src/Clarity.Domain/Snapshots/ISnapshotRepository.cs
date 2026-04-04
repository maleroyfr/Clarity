using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Snapshots;

public interface ISnapshotRepository
{
    Task<Snapshot?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Snapshot>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Snapshot>> GetByEnvironmentAsync(Guid environmentId, CancellationToken ct = default);
    Task<IReadOnlyList<Snapshot>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);
    Task AddAsync(Snapshot snapshot, CancellationToken ct = default);
    Task UpdateAsync(Snapshot snapshot, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IInventoryObjectRepository
{
    Task<IReadOnlyList<InventoryObject>> GetBySnapshotAsync(
        Guid snapshotId,
        InventoryObjectType? type = null,
        CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<InventoryObject> objects, CancellationToken ct = default);
    Task<int> CountBySnapshotAsync(Guid snapshotId, InventoryObjectType type, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
