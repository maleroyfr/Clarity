using Clarity.Domain.Mappings;
using Clarity.Domain.Snapshots;
using Clarity.Exports.Exporters;
using Clarity.SharedContracts.Enums;
using Microsoft.Extensions.Logging;

namespace Clarity.Exports;

public sealed class ExportService(
    ISnapshotRepository snapshotRepository,
    IInventoryObjectRepository inventoryRepository,
    ILogger<ExportService> logger) : IExportService
{
    public async Task<ExportResult> ExportSnapshotAsync(ExportRequest request, CancellationToken ct = default)
    {
        try
        {
            var snapshot = await snapshotRepository.GetByIdAsync(request.SnapshotId, ct)
                ?? throw new InvalidOperationException($"Snapshot '{request.SnapshotId}' was not found.");

            var allObjects = await inventoryRepository.GetBySnapshotAsync(request.SnapshotId, null, ct);
            var filtered   = ApplyFilters(allObjects, request);

            var workloadsIncluded = filtered
                .Select(o => WorkloadAreaMapping.GetWorkloadArea(o.ObjectType).ToString())
                .Distinct()
                .OrderBy(w => w)
                .ToList();

            var metadata = new ExportMetadata(
                snapshot.Id,
                snapshot.Name,
                DateTimeOffset.UtcNow,
                request.Format,
                filtered.Count,
                workloadsIncluded);

            var exportStream = await BuildStreamAsync(filtered, request, metadata, ct);
            var bytesWritten = exportStream.Length;

            if (request.OutputPath is not null)
            {
                var directory = Path.GetDirectoryName(request.OutputPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                await using var file = File.Create(request.OutputPath);
                await exportStream.CopyToAsync(file, ct);
                await exportStream.DisposeAsync();

                logger.LogInformation(
                    "Exported {Count} objects from snapshot {SnapshotId} to {Path} ({Format})",
                    filtered.Count, request.SnapshotId, request.OutputPath, request.Format);

                return new ExportResult(true, request.OutputPath, null, bytesWritten, null, metadata);
            }

            logger.LogInformation(
                "Exported {Count} objects from snapshot {SnapshotId} as stream ({Format})",
                filtered.Count, request.SnapshotId, request.Format);

            return new ExportResult(true, null, exportStream, bytesWritten, null, metadata);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Export failed for snapshot {SnapshotId} (format={Format})",
                request.SnapshotId, request.Format);

            var emptyMeta = new ExportMetadata(
                request.SnapshotId, string.Empty, DateTimeOffset.UtcNow, request.Format, 0, []);

            return new ExportResult(false, null, null, 0, ex.Message, emptyMeta);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<Domain.Snapshots.InventoryObject> ApplyFilters(
        IReadOnlyList<Domain.Snapshots.InventoryObject> objects,
        ExportRequest request)
    {
        IEnumerable<Domain.Snapshots.InventoryObject> result = objects;

        if (request.WorkloadFilter?.Count > 0)
        {
            var allowedTypes = request.WorkloadFilter
                .SelectMany(WorkloadAreaMapping.GetObjectTypes)
                .ToHashSet();
            result = result.Where(o => allowedTypes.Contains(o.ObjectType));
        }

        if (request.ObjectTypeFilter?.Count > 0)
        {
            var allowedTypes = request.ObjectTypeFilter.ToHashSet();
            result = result.Where(o => allowedTypes.Contains(o.ObjectType));
        }

        return result.ToList();
    }

    private static async Task<Stream> BuildStreamAsync(
        IReadOnlyList<Domain.Snapshots.InventoryObject> objects,
        ExportRequest request,
        ExportMetadata metadata,
        CancellationToken ct)
    {
        return request.Format switch
        {
            ExportFormat.Csv  => await new CsvExporter().ExportAsync(objects, request, metadata, ct),
            ExportFormat.Xlsx => new XlsxExporter().Export(objects, request, metadata),
            ExportFormat.Json => await new JsonExporter().ExportAsync(objects, request, metadata, ct),
            _ => throw new NotSupportedException($"Export format '{request.Format}' is not supported.")
        };
    }
}
