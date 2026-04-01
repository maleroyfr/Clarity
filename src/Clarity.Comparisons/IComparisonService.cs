using Clarity.SharedContracts.Enums;

namespace Clarity.Comparisons;

public interface IComparisonService
{
    Task<ComparisonJobResult> RunComparisonAsync(ComparisonRequest request, CancellationToken ct = default);
}

public sealed record ComparisonRequest(
    Guid CustomerId,
    string Name,
    ComparisonMode Mode,
    Guid LeftSnapshotId,
    Guid RightSnapshotId,
    IReadOnlyList<WorkloadArea>? WorkloadFilter = null,
    IReadOnlyList<InventoryObjectType>? ObjectTypeFilter = null);

public sealed record ComparisonJobResult(
    bool Success,
    Guid? ComparisonJobId,
    string? ErrorMessage,
    ComparisonSummary? Summary);

public sealed record ComparisonSummary(
    int TotalLeft,
    int TotalRight,
    int Added,
    int Removed,
    int Modified,
    int Unchanged,
    IReadOnlyList<WorkloadAreaSummary> ByWorkload);

public sealed record WorkloadAreaSummary(
    WorkloadArea WorkloadArea,
    int Added,
    int Removed,
    int Modified,
    int Unchanged);
