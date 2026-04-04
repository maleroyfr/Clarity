namespace Clarity.Domain.Environments;

public interface IEnvironmentRepository
{
    Task<Environment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Environment>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);
    Task AddAsync(Environment environment, CancellationToken ct = default);
    Task UpdateAsync(Environment environment, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IEnvironmentRelationRepository
{
    Task<EnvironmentRelation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<EnvironmentRelation>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);
    Task AddAsync(EnvironmentRelation relation, CancellationToken ct = default);
    Task UpdateAsync(EnvironmentRelation relation, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
