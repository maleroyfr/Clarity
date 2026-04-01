using Clarity.Domain.Common;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Comparisons;

/// <summary>
/// ComparisonJob drives a comparison between two snapshots and owns the results.
/// </summary>
public sealed class ComparisonJob : AggregateRoot
{
    private readonly List<WorkloadArea> _workloadFilter = [];
    private readonly List<InventoryObjectType> _objectTypeFilter = [];
    private readonly List<ComparisonResult> _results = [];

    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = default!;
    public ComparisonMode Mode { get; private set; }
    public Guid LeftSnapshotId { get; private set; }
    public Guid RightSnapshotId { get; private set; }
    public JobStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<WorkloadArea> WorkloadFilter => _workloadFilter.AsReadOnly();
    public IReadOnlyList<InventoryObjectType> ObjectTypeFilter => _objectTypeFilter.AsReadOnly();
    public IReadOnlyList<ComparisonResult> Results => _results.AsReadOnly();

    private ComparisonJob() { } // EF Core

    public static ComparisonJob Create(
        Guid customerId,
        string name,
        ComparisonMode mode,
        Guid leftSnapshotId,
        Guid rightSnapshotId,
        IEnumerable<WorkloadArea>? workloadFilter = null,
        IEnumerable<InventoryObjectType>? objectTypeFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (leftSnapshotId == rightSnapshotId)
            throw new DomainException("COMPARISON_SAME_SNAPSHOT",
                "Left and right snapshots must be different for a meaningful comparison.");

        var job = new ComparisonJob
        {
            CustomerId = customerId,
            Name = name.Trim(),
            Mode = mode,
            LeftSnapshotId = leftSnapshotId,
            RightSnapshotId = rightSnapshotId,
            Status = JobStatus.Queued
        };

        if (workloadFilter != null) job._workloadFilter.AddRange(workloadFilter);
        if (objectTypeFilter != null) job._objectTypeFilter.AddRange(objectTypeFilter);
        return job;
    }

    public void MarkRunning() { Status = JobStatus.Running; MarkUpdated(); }

    public void Complete(IEnumerable<ComparisonResult> results)
    {
        _results.Clear();
        _results.AddRange(results);
        Status = JobStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void Fail() { Status = JobStatus.Failed; CompletedAt = DateTimeOffset.UtcNow; MarkUpdated(); }
}
