using Clarity.Domain.Common;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Comparisons;

public sealed class PropertyDiff : ValueObject
{
    public string PropertyName { get; }
    public string? LeftValue { get; }
    public string? RightValue { get; }

    public PropertyDiff(string propertyName, string? leftValue, string? rightValue)
    {
        PropertyName = propertyName;
        LeftValue = leftValue;
        RightValue = rightValue;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PropertyName;
        yield return LeftValue;
        yield return RightValue;
    }
}

public sealed class ComparisonDeltaItem : ValueObject
{
    private readonly List<PropertyDiff> _changedProperties = [];

    public InventoryObjectType ObjectType { get; }
    public string ExternalId { get; }
    public string? DisplayName { get; }
    public ChangeType ChangeType { get; }
    public string? LeftValueJson { get; }
    public string? RightValueJson { get; }
    public DeltaSeverity Severity { get; }
    public string? ConsultantNote { get; private set; }
    public IReadOnlyList<PropertyDiff> ChangedProperties => _changedProperties.AsReadOnly();

    public ComparisonDeltaItem(
        InventoryObjectType objectType,
        string externalId,
        string? displayName,
        ChangeType changeType,
        DeltaSeverity severity,
        IEnumerable<PropertyDiff>? changedProperties = null,
        string? leftValueJson = null,
        string? rightValueJson = null)
    {
        ObjectType = objectType;
        ExternalId = externalId;
        DisplayName = displayName;
        ChangeType = changeType;
        Severity = severity;
        LeftValueJson = leftValueJson;
        RightValueJson = rightValueJson;
        if (changedProperties != null)
            _changedProperties.AddRange(changedProperties);
    }

    public void SetConsultantNote(string note) => ConsultantNote = note;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ObjectType;
        yield return ExternalId;
        yield return ChangeType;
    }
}

public sealed class ComparisonResult : Entity
{
    private readonly List<ComparisonDeltaItem> _deltaItems = [];

    public Guid ComparisonJobId { get; private set; }
    public WorkloadArea WorkloadArea { get; private set; }
    public int TotalAdded { get; private set; }
    public int TotalRemoved { get; private set; }
    public int TotalModified { get; private set; }
    public int TotalUnchanged { get; private set; }

    public IReadOnlyList<ComparisonDeltaItem> DeltaItems => _deltaItems.AsReadOnly();

    private ComparisonResult() { } // EF Core

    internal static ComparisonResult Create(Guid jobId, WorkloadArea area) =>
        new() { ComparisonJobId = jobId, WorkloadArea = area };

    internal void SetDeltas(IEnumerable<ComparisonDeltaItem> items)
    {
        _deltaItems.Clear();
        _deltaItems.AddRange(items);
        TotalAdded = _deltaItems.Count(d => d.ChangeType == ChangeType.Added);
        TotalRemoved = _deltaItems.Count(d => d.ChangeType == ChangeType.Removed);
        TotalModified = _deltaItems.Count(d => d.ChangeType == ChangeType.Modified);
        TotalUnchanged = _deltaItems.Count(d => d.ChangeType == ChangeType.Unchanged);
        MarkUpdated();
    }
}
