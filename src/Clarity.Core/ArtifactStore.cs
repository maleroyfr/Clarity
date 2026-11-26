namespace Clarity.Core;

public sealed record StoredArtifact(string BlobName, string ContentHash, long Bytes);

public interface IArtifactStore
{
    Task<StoredArtifact> SaveAsync(Guid workspaceId, Stream source, long maxBytes, CancellationToken cancellationToken);
    Task<Stream> OpenAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken);
    Task DeleteAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken);
}
