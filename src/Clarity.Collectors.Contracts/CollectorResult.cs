using Clarity.Domain.Environments;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;

namespace Clarity.Collectors.Contracts;

/// <summary>The normalized result of a completed collector run.</summary>
public sealed class CollectorResult
{
    public CollectorRunStatus Status { get; init; }
    public int ItemsCollected { get; init; }
    public IReadOnlyList<InventoryObject> Objects { get; init; } = [];
    public string? RawPayloadJson { get; init; }
    public IReadOnlyList<string> PermissionsUsed { get; init; } = [];
    public IReadOnlyList<string> CommandsExecuted { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<CollectorError> Errors { get; init; } = [];
    public CollectorMetadata Metadata { get; init; } = default!;

    public static CollectorResult Success(
        IReadOnlyList<InventoryObject> objects,
        IReadOnlyList<string> permissions,
        CollectorMetadata metadata,
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<string>? commands = null,
        string? rawPayloadJson = null) => new()
    {
        Status = CollectorRunStatus.Completed,
        ItemsCollected = objects.Count,
        Objects = objects,
        PermissionsUsed = permissions,
        Metadata = metadata,
        Warnings = warnings ?? [],
        CommandsExecuted = commands ?? [],
        RawPayloadJson = rawPayloadJson
    };

    public static CollectorResult Failure(
        CollectorError error,
        CollectorMetadata metadata,
        IReadOnlyList<string>? commands = null) => new()
    {
        Status = CollectorRunStatus.Failed,
        Errors = [error],
        Metadata = metadata,
        CommandsExecuted = commands ?? []
    };
}

public sealed record CollectorMetadata(
    string CollectorId,
    string Version,
    WorkloadArea WorkloadArea,
    CollectorType CollectorType,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    Guid SnapshotId,
    Guid EnvironmentId);

public sealed record CollectorError(string Code, string Message, string? Detail = null);
