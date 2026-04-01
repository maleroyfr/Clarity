using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Comparisons;

public sealed record ComparisonJobDto(
    Guid Id,
    string Name,
    ComparisonMode Mode,
    Guid LeftSnapshotId,
    Guid RightSnapshotId,
    JobStatus Status,
    ComparisonSummaryDto? Summary);

public sealed record ComparisonSummaryDto(
    int TotalLeft,
    int TotalRight,
    int Added,
    int Removed,
    int Modified,
    int Unchanged,
    IReadOnlyList<WorkloadAreaSummaryDto> ByWorkload);

public sealed record WorkloadAreaSummaryDto(
    WorkloadArea WorkloadArea,
    int Added,
    int Removed,
    int Modified,
    int Unchanged);
