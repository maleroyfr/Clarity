namespace Clarity.Core;

public enum CollectionJobKind { Import, Graph }

public sealed class CollectionJob : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid ActorTenantId { get; init; }
    public Guid ActorObjectId { get; init; }
    public CollectionJobKind Kind { get; init; }
    public required string PayloadJson { get; init; }
    public required string ConcurrencyKey { get; init; }
    public required string CorrelationId { get; init; }
    public CollectionRunState State { get; set; }
    public int Attempt { get; set; }
    public int Progress { get; set; }
    public bool CancellationRequested { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? ErrorCode { get; set; }
}
