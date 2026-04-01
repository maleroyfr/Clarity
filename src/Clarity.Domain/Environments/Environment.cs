using Clarity.Domain.Common;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Environments;

/// <summary>
/// Environment represents a single tenant/directory/AD domain for a Customer.
/// It owns WorkloadSelections and AuthConfigurations.
/// </summary>
public sealed class Environment : AggregateRoot
{
    private readonly List<WorkloadSelection> _workloadSelections = [];
    private readonly List<AuthConfiguration> _authConfigurations = [];
    private readonly List<Tag> _tags = [];

    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public EnvironmentType Type { get; private set; }

    /// <summary>Azure AD / Entra ID tenant GUID (null for on-prem-only environments).</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>Primary domain e.g. contoso.onmicrosoft.com or contoso.com (AD).</summary>
    public string? TenantDomain { get; private set; }

    public EnvironmentStatus Status { get; private set; }

    public IReadOnlyList<WorkloadSelection> WorkloadSelections => _workloadSelections.AsReadOnly();
    public IReadOnlyList<AuthConfiguration> AuthConfigurations => _authConfigurations.AsReadOnly();
    public IReadOnlyList<Tag> Tags => _tags.AsReadOnly();

    private Environment() { } // EF Core

    public static Environment Create(
        Guid customerId,
        string name,
        EnvironmentType type,
        string? description = null,
        Guid? tenantId = null,
        string? tenantDomain = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var env = new Environment
        {
            CustomerId = customerId,
            Name = name.Trim(),
            Description = description?.Trim(),
            Type = type,
            TenantId = tenantId,
            TenantDomain = tenantDomain?.Trim().ToLowerInvariant(),
            Status = EnvironmentStatus.Draft
        };

        env.AddDomainEvent(new EnvironmentCreatedEvent(env.Id, customerId, name, type));
        return env;
    }

    public void Update(string name, string? description, string? tenantDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        TenantDomain = tenantDomain?.Trim().ToLowerInvariant();
        MarkUpdated();
    }

    public void SetStatus(EnvironmentStatus status)
    {
        Status = status;
        MarkUpdated();
    }

    public void Archive()
    {
        Status = EnvironmentStatus.Archived;
        MarkUpdated();
    }

    /// <summary>
    /// Enables or updates workload selections for this environment.
    /// Replaces the existing selections with the provided set.
    /// </summary>
    public void SetWorkloads(IEnumerable<WorkloadArea> enabledWorkloads)
    {
        var newAreas = enabledWorkloads.ToHashSet();

        foreach (var existing in _workloadSelections)
        {
            if (existing.IsEnabled && !newAreas.Contains(existing.WorkloadArea))
                existing.Disable();
            else if (!existing.IsEnabled && newAreas.Contains(existing.WorkloadArea))
                existing.Enable();
        }

        var existingAreas = _workloadSelections.Select(w => w.WorkloadArea).ToHashSet();
        foreach (var area in newAreas.Where(a => !existingAreas.Contains(a)))
            _workloadSelections.Add(WorkloadSelection.Create(Id, area));

        MarkUpdated();
    }

    public WorkloadSelection? GetWorkload(WorkloadArea area) =>
        _workloadSelections.FirstOrDefault(w => w.WorkloadArea == area);

    public void SetAuthConfiguration(AuthConfiguration config)
    {
        var existing = _authConfigurations
            .FirstOrDefault(a => a.WorkloadArea == config.WorkloadArea && a.IsActive);
        existing?.Deactivate();
        _authConfigurations.Add(config);
        MarkUpdated();
    }

    public AuthConfiguration? GetActiveAuthConfig(WorkloadArea area) =>
        _authConfigurations.FirstOrDefault(a => a.WorkloadArea == area && a.IsActive);

    public void AddTag(Tag tag)
    {
        if (!_tags.Contains(tag)) _tags.Add(tag);
    }

    public void RemoveTag(Tag tag) => _tags.Remove(tag);
}
