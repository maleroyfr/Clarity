using Clarity.Domain.Comparisons;
using Clarity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure.Repositories;

internal sealed class ComparisonJobRepository(ClarityDbContext db) : IComparisonJobRepository
{
    public async Task<ComparisonJob?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.ComparisonJobs
            .Include(j => j.Results)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task AddAsync(ComparisonJob job, CancellationToken ct = default) =>
        await db.ComparisonJobs.AddAsync(job, ct);

    public Task UpdateAsync(ComparisonJob job, CancellationToken ct = default)
    {
        db.ComparisonJobs.Update(job);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
