using Clarity.Domain.Environments;

namespace Clarity.Application.Snapshots;

public sealed record SnapshotDto(
    Guid Id,
    Guid CustomerId,
    Guid EnvironmentId,
    string Name,
    string? Description,
    SnapshotStatus Status,
    bool IsImmutable,
    DateTimeOffset? FinalizedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> WorkloadScope,
    int CollectorRunCount);
