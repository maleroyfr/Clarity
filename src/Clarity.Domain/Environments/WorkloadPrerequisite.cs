using Clarity.Domain.Common;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Environments;

public sealed class WorkloadPrerequisite : ValueObject
{
    public string Key { get; }
    public string Description { get; }
    public PrerequisiteCategory Category { get; }
    public bool IsRequired { get; }
    public bool IsCompleted { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }

    public WorkloadPrerequisite(
        string key,
        string description,
        PrerequisiteCategory category,
        bool isRequired = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Description = description;
        Category = category;
        IsRequired = isRequired;
    }

    public WorkloadPrerequisite MarkCompleted()
    {
        IsCompleted = true;
        LastCheckedAt = DateTimeOffset.UtcNow;
        return this;
    }

    public WorkloadPrerequisite MarkFailed()
    {
        IsCompleted = false;
        LastCheckedAt = DateTimeOffset.UtcNow;
        return this;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Key;
        yield return Category;
    }
}
