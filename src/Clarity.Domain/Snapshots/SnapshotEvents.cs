using Clarity.Domain.Common;
using Clarity.Domain.Environments;

namespace Clarity.Domain.Snapshots;

public sealed record SnapshotCreatedEvent(
    Guid SnapshotId,
    Guid CustomerId,
    Guid EnvironmentId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

public sealed record SnapshotFinalizedEvent(
    Guid SnapshotId,
    Guid CustomerId,
    SnapshotStatus Status) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
