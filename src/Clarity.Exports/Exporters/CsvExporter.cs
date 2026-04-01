using System.Globalization;
using Clarity.Domain.Snapshots;
using CsvHelper;

namespace Clarity.Exports.Exporters;

internal sealed class CsvExporter
{
    public async Task<Stream> ExportAsync(
        IReadOnlyList<InventoryObject> objects,
        ExportRequest request,
        ExportMetadata metadata,
        CancellationToken ct)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream, leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            if (request.IncludeMetadata)
            {
                await writer.WriteLineAsync($"#SnapshotId: {metadata.SnapshotId}");
                await writer.WriteLineAsync($"#SnapshotName: {metadata.SnapshotName}");
                await writer.WriteLineAsync($"#ExportedAt: {metadata.ExportedAt:O}");
                await writer.WriteLineAsync($"#Format: {metadata.Format}");
                await writer.WriteLineAsync($"#TotalObjects: {metadata.TotalObjectsExported}");
                await writer.WriteLineAsync($"#Workloads: {string.Join(", ", metadata.WorkloadsIncluded)}");
            }

            var allPropertyKeys = objects
                .SelectMany(o => o.Properties.Keys)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await using (csv.ConfigureAwait(false))
            {
                // Header row
                csv.WriteField("ObjectType");
                csv.WriteField("ExternalId");
                csv.WriteField("DisplayName");
                foreach (var key in allPropertyKeys)
                    csv.WriteField(key);
                if (request.IncludeRawData)
                    csv.WriteField("RawDataJson");
                await csv.NextRecordAsync();

                // Data rows
                foreach (var obj in objects)
                {
                    ct.ThrowIfCancellationRequested();
                    csv.WriteField(obj.ObjectType.ToString());
                    csv.WriteField(obj.ExternalId);
                    csv.WriteField(obj.DisplayName);
                    foreach (var key in allPropertyKeys)
                        csv.WriteField(obj.GetProperty(key));
                    if (request.IncludeRawData)
                        csv.WriteField(obj.RawDataJson);
                    await csv.NextRecordAsync();
                }

                await csv.FlushAsync();
            }
        }

        stream.Position = 0;
        return stream;
    }
}
