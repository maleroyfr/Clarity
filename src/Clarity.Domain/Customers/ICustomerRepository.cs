using Clarity.Domain.Common;

namespace Clarity.Domain.Customers;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetAllAsync(bool includeArchived = false, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Customer customer, CancellationToken ct = default);
    Task UpdateAsync(Customer customer, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
