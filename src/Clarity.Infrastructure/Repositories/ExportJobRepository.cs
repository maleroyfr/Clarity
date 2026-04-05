using Clarity.Application.Common.Exceptions;
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

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.ExportJobs.FindAsync([id], ct)
            ?? throw new NotFoundException(nameof(ExportJob), id);
        db.ExportJobs.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
