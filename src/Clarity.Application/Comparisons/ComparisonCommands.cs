using Clarity.Application.Common;
using Clarity.Comparisons;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;

namespace Clarity.Application.Comparisons;

public sealed record RunComparisonCommand(
    Guid CustomerId,
    string Name,
    ComparisonMode Mode,
    Guid LeftSnapshotId,
    Guid RightSnapshotId,
    IReadOnlyList<WorkloadArea>? WorkloadFilter) : ICommand<ComparisonJobDto>;

public sealed class RunComparisonHandler(
    IComparisonService comparisonService,
    ILogger<RunComparisonHandler> logger)
    : ICommandHandler<RunComparisonCommand, ComparisonJobDto>
{
    public async Task<ComparisonJobDto> Handle(RunComparisonCommand cmd, CancellationToken ct)
    {
        var request = new ComparisonRequest(
            cmd.CustomerId,
            cmd.Name,
            cmd.Mode,
            cmd.LeftSnapshotId,
            cmd.RightSnapshotId,
            cmd.WorkloadFilter);

        var result = await comparisonService.RunComparisonAsync(request, ct);

        if (!result.Success)
            logger.LogWarning("Comparison job {JobId} failed: {Error}", result.ComparisonJobId, result.ErrorMessage);

        ComparisonSummaryDto? summaryDto = null;
        if (result.Summary is { } s)
        {
            summaryDto = new ComparisonSummaryDto(
                s.TotalLeft,
                s.TotalRight,
                s.Added,
                s.Removed,
                s.Modified,
                s.Unchanged,
                s.ByWorkload
                    .Select(w => new WorkloadAreaSummaryDto(w.WorkloadArea, w.Added, w.Removed, w.Modified, w.Unchanged))
                    .ToList());
        }

        var status = result.Success
            ? JobStatus.Completed
            : (result.ComparisonJobId.HasValue ? JobStatus.Failed : JobStatus.Queued);

        return new ComparisonJobDto(
            result.ComparisonJobId ?? Guid.Empty,
            cmd.Name,
            cmd.Mode,
            cmd.LeftSnapshotId,
            cmd.RightSnapshotId,
            status,
            summaryDto);
    }
}
