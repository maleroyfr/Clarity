namespace Clarity.Core;

public sealed record StoredArtifact(string BlobName, string ContentHash, long Bytes);

/// <summary>A stored file of one workspace prefix, listed by the retention sweep to find files no metadata row references.</summary>
public sealed record StoredFile(string BlobName, DateTimeOffset LastModifiedUtc);

public interface IArtifactStore
{
    Task<StoredArtifact> SaveAsync(Guid workspaceId, Stream source, long maxBytes, CancellationToken cancellationToken);
    Task<Stream> OpenAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken);
    Task DeleteAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken);
    IAsyncEnumerable<StoredFile> ListAsync(Guid workspaceId, CancellationToken cancellationToken);
}
