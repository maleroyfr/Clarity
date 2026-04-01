using Clarity.Domain.Common;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Exports;

public sealed class ExportProfile : Entity
{
    private readonly List<WorkloadArea> _includedWorkloads = [];
    private readonly List<InventoryObjectType> _includedObjectTypes = [];

    public Guid? CustomerId { get; private set; } // null = system default profile
    public string Name { get; private set; } = default!;
    public bool IncludeRawData { get; private set; }
    public bool IncludeMetadata { get; private set; }
    public bool IncludeSummarySheet { get; private set; }

    public IReadOnlyList<WorkloadArea> IncludedWorkloads => _includedWorkloads.AsReadOnly();
    public IReadOnlyList<InventoryObjectType> IncludedObjectTypes => _includedObjectTypes.AsReadOnly();

    private ExportProfile() { } // EF Core

    public static ExportProfile CreateDefault() =>
        new()
        {
            Name = "Default",
            IncludeRawData = false,
            IncludeMetadata = true,
            IncludeSummarySheet = true
        };

    public static ExportProfile Create(
        Guid customerId,
        string name,
        IEnumerable<WorkloadArea> workloads,
        bool includeRawData = false,
        bool includeSummarySheet = true)
    {
        var profile = new ExportProfile
        {
            CustomerId = customerId,
            Name = name,
            IncludeRawData = includeRawData,
            IncludeMetadata = true,
            IncludeSummarySheet = includeSummarySheet
        };
        profile._includedWorkloads.AddRange(workloads);
        return profile;
    }
}

public sealed class ExportJob : AggregateRoot
{
    private readonly List<Guid> _snapshotIds = [];

    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = default!;
    public ExportFormat Format { get; private set; }
    public Guid? ExportProfileId { get; private set; }
    public Guid? ComparisonJobId { get; private set; }
    public JobStatus Status { get; private set; }
    public string? OutputPath { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<Guid> SnapshotIds => _snapshotIds.AsReadOnly();

    private ExportJob() { } // EF Core

    public static ExportJob Create(
        Guid customerId,
        string name,
        IEnumerable<Guid> snapshotIds,
        ExportFormat format,
        Guid? exportProfileId = null,
        Guid? comparisonJobId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var job = new ExportJob
        {
            CustomerId = customerId,
            Name = name.Trim(),
            Format = format,
            ExportProfileId = exportProfileId,
            ComparisonJobId = comparisonJobId,
            Status = JobStatus.Queued
        };
        job._snapshotIds.AddRange(snapshotIds);
        return job;
    }

    public void MarkRunning() { Status = JobStatus.Running; MarkUpdated(); }

    public void Complete(string outputPath)
    {
        OutputPath = outputPath;
        Status = JobStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void Fail() { Status = JobStatus.Failed; CompletedAt = DateTimeOffset.UtcNow; MarkUpdated(); }
}
