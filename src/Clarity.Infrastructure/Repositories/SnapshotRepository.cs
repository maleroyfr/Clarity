using Clarity.Application.Common.Exceptions;
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

    public async Task<IReadOnlyList<Snapshot>> GetAllAsync(CancellationToken ct = default)
    {
        // Client-side ordering: SQLite does not support ORDER BY on DateTimeOffset columns
        var snapshots = await db.Snapshots.ToListAsync(ct);
        return snapshots.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<Snapshot>> GetByEnvironmentAsync(
        Guid environmentId, CancellationToken ct = default)
    {
        var snapshots = await db.Snapshots
            .Where(s => s.EnvironmentId == environmentId)
            .ToListAsync(ct);
        return snapshots.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<Snapshot>> GetByCustomerAsync(
        Guid customerId, CancellationToken ct = default)
    {
        var snapshots = await db.Snapshots
            .Where(s => s.CustomerId == customerId)
            .ToListAsync(ct);
        return snapshots.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task AddAsync(Snapshot snapshot, CancellationToken ct = default) =>
        await db.Snapshots.AddAsync(snapshot, ct);

    public Task UpdateAsync(Snapshot snapshot, CancellationToken ct = default)
    {
        db.Snapshots.Update(snapshot);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var snapshot = await db.Snapshots.FindAsync([id], ct);
        if (snapshot is not null)
            db.Snapshots.Remove(snapshot);
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
