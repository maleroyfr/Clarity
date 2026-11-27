using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Clarity.Core;

namespace Clarity.Infrastructure;

/// <summary>Generated exports in the private exports Blob container, under the workspace and export prefix.</summary>
public sealed class BlobExportStore(BlobServiceClient? blobs = null) : IExportStore
{
    public bool IsConfigured => blobs is not null;

    private BlobContainerClient Container => (blobs ?? throw new DiscoveryConfigurationException()).GetBlobContainerClient("exports");

    public async Task<StoredArtifact> SaveAsync(Guid workspaceId, Guid exportId, string extension, string contentType, Stream source,
        CancellationToken cancellationToken)
    {
        if (extension is not (".xlsx" or ".csv")) { throw new ArgumentException("Unsupported export file type."); }
        var name = $"{workspaceId:N}/{exportId:N}/{Guid.NewGuid():N}{extension}";
        var blob = Container.GetBlobClient(name);
        try
        {
            await using var target = await blob.OpenWriteAsync(false,
                new BlobOpenWriteOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            long bytes = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
            {
                bytes += count;
                hash.AppendData(buffer, 0, count);
                await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            await target.FlushAsync(cancellationToken);
            return new StoredArtifact(name, Convert.ToHexStringLower(hash.GetHashAndReset()), bytes);
        }
        catch
        {
            await blob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
            throw;
        }
    }

    public async Task<Stream> OpenAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken)
    {
        DemandPrefix(workspaceId, blobName);
        try
        {
            return await Container.GetBlobClient(blobName).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new FileExpiredException("FileMissing");
        }
    }

    public async IAsyncEnumerable<StoredFile> ListAsync(Guid workspaceId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in Container.GetBlobsAsync(new GetBlobsOptions { Prefix = $"{workspaceId:N}/" }, cancellationToken))
        {
            yield return new StoredFile(item.Name, item.Properties.LastModified ?? DateTimeOffset.MinValue);
        }
    }

    public Task DeleteAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken)
    {
        DemandPrefix(workspaceId, blobName);
        return Container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<int> PurgeExportAsync(Guid workspaceId, Guid exportId, IReadOnlySet<string> keep, CancellationToken cancellationToken)
    {
        var container = Container;
        var deleted = 0;
        await foreach (var item in container.GetBlobsAsync(new GetBlobsOptions { Prefix = $"{workspaceId:N}/{exportId:N}/" }, cancellationToken))
        {
            if (!keep.Contains(item.Name) && await container.GetBlobClient(item.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken)) { deleted++; }
        }
        return deleted;
    }

    private static void DemandPrefix(Guid workspaceId, string name)
    {
        if (!name.StartsWith($"{workspaceId:N}/", StringComparison.Ordinal) || name.Contains("..", StringComparison.Ordinal))
        {
            throw new AccessDeniedException();
        }
    }
}
