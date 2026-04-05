using Clarity.Application.Common.Exceptions;
using Clarity.Domain.Environments;
using Clarity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure.Repositories;

internal sealed class EnvironmentRepository(ClarityDbContext db) : IEnvironmentRepository
{
    public async Task<Domain.Environments.Environment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Environments
            .Include(e => e.WorkloadSelections)
            .Include(e => e.AuthConfigurations)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<Domain.Environments.Environment>> GetByCustomerAsync(
        Guid customerId, CancellationToken ct = default) =>
        await db.Environments
            .Where(e => e.CustomerId == customerId)
            .Include(e => e.WorkloadSelections)
            .OrderBy(e => e.Name)
            .ToListAsync(ct);

    public async Task AddAsync(Domain.Environments.Environment environment, CancellationToken ct = default) =>
        await db.Environments.AddAsync(environment, ct);

    public Task UpdateAsync(Domain.Environments.Environment environment, CancellationToken ct = default)
    {
        db.Environments.Update(environment);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var env = await db.Environments.FindAsync([id], ct);
        if (env != null) db.Environments.Remove(env);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

internal sealed class EnvironmentRelationRepository(ClarityDbContext db) : IEnvironmentRelationRepository
{
    public async Task<EnvironmentRelation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.EnvironmentRelations.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<EnvironmentRelation>> GetByCustomerAsync(
        Guid customerId, CancellationToken ct = default) =>
        await db.EnvironmentRelations
            .Where(r => r.CustomerId == customerId)
            .ToListAsync(ct);

    public async Task AddAsync(EnvironmentRelation relation, CancellationToken ct = default) =>
        await db.EnvironmentRelations.AddAsync(relation, ct);

    public Task UpdateAsync(EnvironmentRelation relation, CancellationToken ct = default)
    {
        db.EnvironmentRelations.Update(relation);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var relation = await db.EnvironmentRelations.FindAsync([id], ct);
        if (relation != null) db.EnvironmentRelations.Remove(relation);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
