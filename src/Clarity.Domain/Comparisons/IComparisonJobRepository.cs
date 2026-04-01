namespace Clarity.Domain.Comparisons;

public interface IComparisonJobRepository
{
    Task<ComparisonJob?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ComparisonJob job, CancellationToken ct = default);
    Task UpdateAsync(ComparisonJob job, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
