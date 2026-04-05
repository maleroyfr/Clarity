using Clarity.Application.Common.Exceptions;
using Clarity.Domain.Customers;
using Clarity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure.Repositories;

internal sealed class CustomerRepository(ClarityDbContext db) : ICustomerRepository
{
    public async Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Customer>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var query = db.Customers.AsQueryable();
        if (!includeArchived) query = query.Where(c => !c.IsArchived);
        return await query.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        await db.Customers.AnyAsync(c => c.Id == id, ct);

    public async Task AddAsync(Customer customer, CancellationToken ct = default) =>
        await db.Customers.AddAsync(customer, ct);

    public Task UpdateAsync(Customer customer, CancellationToken ct = default)
    {
        db.Customers.Update(customer);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.Customers.FindAsync([id], ct)
            ?? throw new NotFoundException(nameof(Customer), id);
        db.Customers.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
