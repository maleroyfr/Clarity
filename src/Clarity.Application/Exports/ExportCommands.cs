using Clarity.Application.Common;
using Clarity.Application.Common.Exceptions;
using Clarity.Domain.Exports;
using Clarity.Domain.Snapshots;
using Clarity.Exports;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;

namespace Clarity.Application.Exports;

public sealed record CreateExportJobCommand(
    Guid SnapshotId,
    ExportFormat Format,
    string? OutputPath,
    IReadOnlyList<WorkloadArea>? WorkloadFilter) : ICommand<ExportJobDto>;

public sealed class CreateExportJobHandler(
    IExportService exportService,
    ISnapshotRepository snapshotRepository,
    IExportJobRepository exportJobRepository,
    ILogger<CreateExportJobHandler> logger)
    : ICommandHandler<CreateExportJobCommand, ExportJobDto>
{
    public async Task<ExportJobDto> Handle(CreateExportJobCommand cmd, CancellationToken ct)
    {
        var snapshot = await snapshotRepository.GetByIdAsync(cmd.SnapshotId, ct)
            ?? throw new NotFoundException(nameof(Snapshot), cmd.SnapshotId);

        var jobName = $"Export_{snapshot.Name}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        var job = ExportJob.Create(
            snapshot.CustomerId,
            jobName,
            [cmd.SnapshotId],
            cmd.Format);

        job.MarkRunning();
        await exportJobRepository.AddAsync(job, ct);
        await exportJobRepository.SaveChangesAsync(ct);

        var request = new ExportRequest(
            cmd.SnapshotId,
            cmd.Format,
            cmd.OutputPath,
            cmd.WorkloadFilter,
            ObjectTypeFilter: null);

        var result = await exportService.ExportSnapshotAsync(request, ct);

        if (result.Success)
            job.Complete(result.FilePath ?? "stream");
        else
            job.Fail();

        await exportJobRepository.UpdateAsync(job, ct);
        await exportJobRepository.SaveChangesAsync(ct);

        if (!result.Success)
            logger.LogWarning("Export job {JobId} failed: {Error}", job.Id, result.ErrorMessage);

        return new ExportJobDto(
            job.Id,
            job.Status,
            result.FilePath,
            result.BytesWritten,
            result.ErrorMessage);
    }
}
