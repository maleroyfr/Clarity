using Clarity.Domain.Common;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Snapshots;

public sealed class CollectorError : ValueObject
{
    public string Code { get; }
    public string Message { get; }
    public string? Detail { get; }
    public DateTimeOffset OccurredAt { get; }

    public CollectorError(string code, string message, string? detail = null)
    {
        Code = code;
        Message = message;
        Detail = detail;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
        yield return Message;
        yield return OccurredAt;
    }
}

public sealed class CollectorRun : Entity
{
    private readonly List<string> _permissionsUsed = [];
    private readonly List<string> _commandsExecuted = [];
    private readonly List<string> _warnings = [];
    private readonly List<CollectorError> _errors = [];

    public Guid SnapshotId { get; private set; }
    public WorkloadArea WorkloadArea { get; private set; }
    public CollectorType CollectorType { get; private set; }
    public string CollectorVersion { get; private set; } = default!;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public CollectorRunStatus Status { get; private set; }
    public int ItemsCollected { get; private set; }

    public IReadOnlyList<string> PermissionsUsed => _permissionsUsed.AsReadOnly();
    public IReadOnlyList<string> CommandsExecuted => _commandsExecuted.AsReadOnly();
    public IReadOnlyList<string> Warnings => _warnings.AsReadOnly();
    public IReadOnlyList<CollectorError> Errors => _errors.AsReadOnly();

    private CollectorRun() { } // EF Core

    internal static CollectorRun Start(
        Guid snapshotId,
        WorkloadArea area,
        CollectorType collectorType,
        string version)
    {
        return new CollectorRun
        {
            SnapshotId = snapshotId,
            WorkloadArea = area,
            CollectorType = collectorType,
            CollectorVersion = version,
            Status = CollectorRunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
    }

    public void Complete(int itemsCollected, IEnumerable<string> permissions, IEnumerable<string>? commands = null)
    {
        Status = CollectorRunStatus.Completed;
        ItemsCollected = itemsCollected;
        CompletedAt = DateTimeOffset.UtcNow;
        _permissionsUsed.AddRange(permissions);
        if (commands != null) _commandsExecuted.AddRange(commands);
        MarkUpdated();
    }

    public void Fail(CollectorError error, IEnumerable<string>? commands = null)
    {
        Status = CollectorRunStatus.Failed;
        CompletedAt = DateTimeOffset.UtcNow;
        _errors.Add(error);
        if (commands != null) _commandsExecuted.AddRange(commands);
        MarkUpdated();
    }

    public void Cancel()
    {
        Status = CollectorRunStatus.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void AddWarning(string message) => _warnings.Add(message);
    public void AddError(CollectorError error) => _errors.Add(error);
}
