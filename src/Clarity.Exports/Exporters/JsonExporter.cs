using System.Text.Json;
using Clarity.Domain.Snapshots;

namespace Clarity.Exports.Exporters;

internal sealed class JsonExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<Stream> ExportAsync(
        IReadOnlyList<InventoryObject> objects,
        ExportRequest request,
        ExportMetadata metadata,
        CancellationToken ct)
    {
        var document = new JsonDocument(
            new JsonMetadata(
                metadata.SnapshotId,
                metadata.SnapshotName,
                metadata.ExportedAt,
                metadata.Format.ToString(),
                metadata.TotalObjectsExported,
                metadata.WorkloadsIncluded),
            objects.Select(o => new JsonInventoryObject(
                o.ObjectType.ToString(),
                o.ExternalId,
                o.DisplayName,
                o.Properties.ToDictionary(kv => kv.Key, kv => kv.Value),
                request.IncludeRawData ? o.RawDataJson : null)).ToList());

        var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, document, Options, ct);
        stream.Position = 0;
        return stream;
    }

    private sealed record JsonDocument(
        JsonMetadata Metadata,
        IReadOnlyList<JsonInventoryObject> Objects);

    private sealed record JsonMetadata(
        Guid SnapshotId,
        string SnapshotName,
        DateTimeOffset ExportedAt,
        string Format,
        int TotalObjectsExported,
        IReadOnlyList<string> WorkloadsIncluded);

    private sealed record JsonInventoryObject(
        string ObjectType,
        string ExternalId,
        string? DisplayName,
        Dictionary<string, string?> Properties,
        string? RawDataJson);
}
