using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Clarity.Contracts;

namespace Clarity.Infrastructure;

public sealed record PackageLimits(
    long CompressedBytes = 16 * 1024 * 1024,
    long UncompressedBytes = 128 * 1024 * 1024,
    long EntryBytes = 64 * 1024 * 1024,
    int Lines = 150000,
    int LineCharacters = 65536);
public sealed record ReadPackage(DiscoveryPackage Package, long UncompressedBytes, string ContentHash);

public sealed class ZipPackageReader(PackageValidator validator, PackageLimits limits)
{
    public async Task<ReadPackage> ReadAsync(Stream source, string? expectedHash = null, CancellationToken cancellationToken = default)
    {
        using var compressed = new MemoryStream();
        await using (var bounded = new LimitedReadStream(source, limits.CompressedBytes, leaveOpen: true))
        {
            await bounded.CopyToAsync(compressed, cancellationToken);
        }
        var hash = Convert.ToHexStringLower(SHA256.HashData(compressed.GetBuffer().AsSpan(0, (int)compressed.Length)));
        if (expectedHash is not null && hash != expectedHash)
        {
            throw new ContractException("ContentHashMismatch", "zip");
        }
        compressed.Position = 0;
        try
        {
            using var zip = new ZipArchive(compressed, ZipArchiveMode.Read);
            var expected = new HashSet<string>(["manifest.json", "objects.jsonl", "relationships.jsonl", "results.json"], StringComparer.Ordinal);
            if (zip.Entries.Count != 4 || zip.Entries.Any(x => !expected.Remove(x.FullName)))
            {
                throw new ContractException("RootEntries", "zip");
            }
            long uncompressed = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.Length > limits.EntryBytes || entry.Length > limits.UncompressedBytes - uncompressed)
                {
                    throw new ContractException("UncompressedSizeLimit", entry.FullName);
                }
                uncompressed += entry.Length;
            }
            var manifest = validator.Read<PackageManifest>(await ReadDocumentAsync(zip.GetEntry("manifest.json")!, cancellationToken), "manifest.json");
            var results = validator.Read<SectionResult[]>(await ReadDocumentAsync(zip.GetEntry("results.json")!, cancellationToken), "results.json");
            var objects = await ReadLinesAsync<ObjectRecord>(zip.GetEntry("objects.jsonl")!, cancellationToken);
            var relations = await ReadLinesAsync<RelationshipRecord>(zip.GetEntry("relationships.jsonl")!, cancellationToken);
            if (objects.Count + relations.Count > limits.Lines)
            {
                throw new ContractException("LineLimit", "zip");
            }
            var package = new DiscoveryPackage(manifest, objects, relations, results);
            PackageValidator.ValidateReferences(package);
            return new ReadPackage(package, uncompressed, hash);
        }
        catch (InvalidDataException)
        {
            throw new ContractException("ZipStructure", "zip");
        }
        catch (DecoderFallbackException)
        {
            throw new ContractException("Utf8", "zip");
        }
    }

    private static StreamReader Reader(Stream stream) => new(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);

    private async Task<string> ReadDocumentAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = new LimitedReadStream(entry.Open(), Math.Min(limits.EntryBytes, 2 * 1024 * 1024));
        using var reader = Reader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private async Task<List<T>> ReadLinesAsync<T>(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = new LimitedReadStream(entry.Open(), limits.EntryBytes);
        using var reader = Reader(stream);
        var records = new List<T>();
        var buffer = new char[4096];
        var line = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            foreach (var value in buffer.AsSpan(0, count))
            {
                if (value == '\n')
                {
                    AddLine();
                }
                else
                {
                    line.Append(value);
                    if (line.Length > limits.LineCharacters)
                    {
                        throw new ContractException("LineLength", entry.FullName, records.Count + 1);
                    }
                }
            }
        }
        if (line.Length > 0) { AddLine(); }
        return records;

        void AddLine()
        {
            if (records.Count >= limits.Lines)
            {
                throw new ContractException("LineLimit", entry.FullName);
            }
            if (string.IsNullOrWhiteSpace(line.ToString()))
            {
                throw new ContractException("EmptyLine", entry.FullName, records.Count + 1);
            }
            records.Add(validator.Read<T>(line.ToString(), entry.FullName, records.Count + 1));
            line.Clear();
        }
    }
}

internal sealed class LimitedReadStream(Stream inner, long limit, bool leaveOpen = false) : Stream
{
    private long bytes;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => bytes; set => throw new NotSupportedException(); }
    private int Count(int count)
    {
        bytes += count;
        if (bytes > limit) { throw new ContractException("SizeLimit", "zip"); }
        return count;
    }
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen) { inner.Dispose(); }
        base.Dispose(disposing);
    }
}
