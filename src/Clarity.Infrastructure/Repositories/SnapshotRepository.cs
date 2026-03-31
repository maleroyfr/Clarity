using Clarity.Domain.Snapshots;
using Clarity.Infrastructure.Persistence;
using Clarity.SharedContracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure.Repositories;

internal sealed class SnapshotRepository(ClarityDbContext db) : ISnapshotRepository
{
    public async Task<Snapshot?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Snapshots
            .Include(s => s.CollectorRuns)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Snapshot>> GetByEnvironmentAsync(
        Guid environmentId, CancellationToken ct = default) =>
        await db.Snapshots
            .Where(s => s.EnvironmentId == environmentId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Snapshot>> GetByCustomerAsync(
        Guid customerId, CancellationToken ct = default) =>
        await db.Snapshots
            .Where(s => s.CustomerId == customerId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Snapshot snapshot, CancellationToken ct = default) =>
        await db.Snapshots.AddAsync(snapshot, ct);

    public Task UpdateAsync(Snapshot snapshot, CancellationToken ct = default)
    {
        db.Snapshots.Update(snapshot);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

internal sealed class InventoryObjectRepository(ClarityDbContext db) : IInventoryObjectRepository
{
    public async Task<IReadOnlyList<InventoryObject>> GetBySnapshotAsync(
        Guid snapshotId,
        InventoryObjectType? type = null,
        CancellationToken ct = default)
    {
        var query = db.InventoryObjects.Where(o => o.SnapshotId == snapshotId);
        if (type.HasValue) query = query.Where(o => o.ObjectType == type.Value);
        return await query.ToListAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<InventoryObject> objects, CancellationToken ct = default) =>
        await db.InventoryObjects.AddRangeAsync(objects, ct);

    public async Task<int> CountBySnapshotAsync(Guid snapshotId, InventoryObjectType type, CancellationToken ct = default) =>
        await db.InventoryObjects.CountAsync(o => o.SnapshotId == snapshotId && o.ObjectType == type, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
