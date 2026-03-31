using Clarity.Domain.Common;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Snapshots;

/// <summary>
/// Snapshot is an immutable point-in-time record of a customer environment.
/// Once finalized it cannot be modified — new collection creates a new snapshot.
/// </summary>
public sealed class Snapshot : AggregateRoot
{
    private readonly List<WorkloadArea> _workloadScope = [];
    private readonly List<CollectorRun> _collectorRuns = [];

    public Guid CustomerId { get; private set; }
    public Guid EnvironmentId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public SnapshotStatus Status { get; private set; }

    /// <summary>When true, the snapshot is sealed — no further modifications allowed.</summary>
    public bool IsImmutable { get; private set; }

    public DateTimeOffset? FinalizedAt { get; private set; }
    public IReadOnlyList<WorkloadArea> WorkloadScope => _workloadScope.AsReadOnly();
    public IReadOnlyList<CollectorRun> CollectorRuns => _collectorRuns.AsReadOnly();

    private Snapshot() { } // EF Core

    public static Snapshot Create(
        Guid customerId,
        Guid environmentId,
        string name,
        IEnumerable<WorkloadArea> scope,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var snapshot = new Snapshot
        {
            CustomerId = customerId,
            EnvironmentId = environmentId,
            Name = name.Trim(),
            Description = description?.Trim(),
            Status = SnapshotStatus.Draft,
            IsImmutable = false
        };

        snapshot._workloadScope.AddRange(scope.Distinct());
        snapshot.AddDomainEvent(new SnapshotCreatedEvent(snapshot.Id, customerId, environmentId));
        return snapshot;
    }

    public CollectorRun StartCollectorRun(WorkloadArea area, CollectorType collectorType, string version)
    {
        EnsureNotImmutable();
        if (Status == SnapshotStatus.Draft) Status = SnapshotStatus.Running;

        var run = CollectorRun.Start(Id, area, collectorType, version);
        _collectorRuns.Add(run);
        MarkUpdated();
        return run;
    }

    /// <summary>Seals the snapshot as immutable. Called after all collector runs complete.</summary>
    public void Seal()
    {
        EnsureNotImmutable();

        var anyFailed = _collectorRuns.Any(r => r.Status == CollectorRunStatus.Failed);
        Status = anyFailed ? SnapshotStatus.Partial : SnapshotStatus.Completed;
        IsImmutable = true;
        FinalizedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
        AddDomainEvent(new SnapshotFinalizedEvent(Id, CustomerId, Status));
    }

    public void MarkFailed(string reason)
    {
        EnsureNotImmutable();
        Status = SnapshotStatus.Failed;
        MarkUpdated();
    }

    private void EnsureNotImmutable()
    {
        if (IsImmutable)
            throw new DomainException("SNAPSHOT_IMMUTABLE", $"Snapshot '{Id}' is immutable and cannot be modified.");
    }
}
