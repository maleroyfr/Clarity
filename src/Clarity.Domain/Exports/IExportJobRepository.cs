namespace Clarity.Domain.Exports;

public interface IExportJobRepository
{
    Task<ExportJob?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ExportJob job, CancellationToken ct = default);
    Task UpdateAsync(ExportJob job, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
