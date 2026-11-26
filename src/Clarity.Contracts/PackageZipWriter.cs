using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Clarity.Contracts;

public static class PackageZipWriter
{
    public static async Task WriteAsync(DiscoveryPackage package, Stream destination, CancellationToken cancellationToken = default)
    {
        new PackageValidator().Validate(package);
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        await WriteDocumentAsync("manifest.json", package.Manifest);
        await WriteDocumentAsync("results.json", package.Results);
        await WriteLinesAsync("objects.jsonl", package.Objects);
        await WriteLinesAsync("relationships.jsonl", package.Relationships);
        return;

        async Task WriteDocumentAsync<T>(string name, T value)
        {
            await using var output = zip.CreateEntry(name).Open();
            await JsonSerializer.SerializeAsync(output, value, ContractJson.Options, cancellationToken);
        }

        async Task WriteLinesAsync<T>(string name, IEnumerable<T> values)
        {
            await using var output = zip.CreateEntry(name).Open();
            await using var writer = new StreamWriter(output, new UTF8Encoding(false));
            foreach (var value in values)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(value, ContractJson.Options).AsMemory(), cancellationToken);
            }
        }
    }
}
