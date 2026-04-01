using Clarity.Domain.Common;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Environments;

public sealed class WorkloadSelection : Entity
{
    private readonly List<WorkloadPrerequisite> _prerequisites = [];

    public Guid EnvironmentId { get; private set; }
    public WorkloadArea WorkloadArea { get; private set; }
    public bool IsEnabled { get; private set; }
    public WorkloadConfigStatus ConfigStatus { get; private set; }
    public IReadOnlyList<WorkloadPrerequisite> Prerequisites => _prerequisites.AsReadOnly();

    private WorkloadSelection() { } // EF Core

    internal static WorkloadSelection Create(Guid environmentId, WorkloadArea area, bool enabled = true)
    {
        return new WorkloadSelection
        {
            EnvironmentId = environmentId,
            WorkloadArea = area,
            IsEnabled = enabled,
            ConfigStatus = WorkloadConfigStatus.NotConfigured
        };
    }

    public void Enable() { IsEnabled = true; MarkUpdated(); }
    public void Disable() { IsEnabled = false; MarkUpdated(); }

    public void SetConfigStatus(WorkloadConfigStatus status)
    {
        ConfigStatus = status;
        MarkUpdated();
    }

    public void SetPrerequisites(IEnumerable<WorkloadPrerequisite> prerequisites)
    {
        _prerequisites.Clear();
        _prerequisites.AddRange(prerequisites);
        MarkUpdated();
    }

    public bool AllRequiredPrerequisitesMet() =>
        _prerequisites.Where(p => p.IsRequired).All(p => p.IsCompleted);
}
