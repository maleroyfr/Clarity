using Clarity.Domain.Exports;
using Clarity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure.Repositories;

internal sealed class ExportJobRepository(ClarityDbContext db) : IExportJobRepository
{
    public async Task<ExportJob?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.ExportJobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task AddAsync(ExportJob job, CancellationToken ct = default) =>
        await db.ExportJobs.AddAsync(job, ct);

    public Task UpdateAsync(ExportJob job, CancellationToken ct = default)
    {
        db.ExportJobs.Update(job);
        return Task.CompletedTask;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
