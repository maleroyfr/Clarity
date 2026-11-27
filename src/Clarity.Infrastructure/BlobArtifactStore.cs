using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Clarity.Contracts;
using Clarity.Core;

namespace Clarity.Infrastructure;

public sealed class BlobArtifactStore(BlobServiceClient? blobs = null) : IArtifactStore
{
    private BlobContainerClient Container => (blobs ?? throw new DiscoveryConfigurationException()).GetBlobContainerClient("raw-artifacts");

    public async Task<StoredArtifact> SaveAsync(Guid workspaceId, Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        var name = $"{workspaceId:N}/{Guid.NewGuid():N}.zip";
        var blob = Container.GetBlobClient(name);
        try
        {
            await using var target = await blob.OpenWriteAsync(false,
                new BlobOpenWriteOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/zip" } }, cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            long bytes = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
            {
                bytes += count;
                if (bytes > maxBytes)
                {
                    throw new ContractException("CompressedSizeLimit", "zip");
                }
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

    private static void DemandPrefix(Guid workspaceId, string name)
    {
        if (!name.StartsWith($"{workspaceId:N}/", StringComparison.Ordinal) || name.Contains("..", StringComparison.Ordinal))
        {
            throw new AccessDeniedException();
        }
    }
}

public sealed class DiscoveryConfigurationException : Exception
{
    public DiscoveryConfigurationException() : base("Discovery storage is not configured.") { }
}
