using Clarity.SharedContracts.Enums;

namespace Clarity.Exports;

public interface IExportService
{
    Task<ExportResult> ExportSnapshotAsync(ExportRequest request, CancellationToken ct = default);
}

public sealed record ExportRequest(
    Guid SnapshotId,
    ExportFormat Format,
    string? OutputPath,
    IReadOnlyList<WorkloadArea>? WorkloadFilter,
    IReadOnlyList<InventoryObjectType>? ObjectTypeFilter,
    bool IncludeMetadata = true,
    bool IncludeRawData = false);

public sealed record ExportResult(
    bool Success,
    string? FilePath,
    Stream? Stream,
    long BytesWritten,
    string? ErrorMessage,
    ExportMetadata Metadata);

public sealed record ExportMetadata(
    Guid SnapshotId,
    string SnapshotName,
    DateTimeOffset ExportedAt,
    ExportFormat Format,
    int TotalObjectsExported,
    IReadOnlyList<string> WorkloadsIncluded);
