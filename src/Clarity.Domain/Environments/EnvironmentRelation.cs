using Clarity.Domain.Common;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Environments;

/// <summary>
/// Declares a relationship between two environments (e.g. M&amp;A, migration, coexistence).
/// </summary>
public sealed class EnvironmentRelation : AggregateRoot
{
    private readonly List<Tag> _tags = [];

    public Guid CustomerId { get; private set; }
    public Guid SourceEnvironmentId { get; private set; }
    public Guid TargetEnvironmentId { get; private set; }
    public RelationType RelationType { get; private set; }
    public RelationDirection Direction { get; private set; }
    public string? Notes { get; private set; }
    public IReadOnlyList<Tag> Tags => _tags.AsReadOnly();

    private EnvironmentRelation() { } // EF Core

    public static EnvironmentRelation Create(
        Guid customerId,
        Guid sourceEnvironmentId,
        Guid targetEnvironmentId,
        RelationType relationType,
        RelationDirection direction = RelationDirection.Bidirectional,
        string? notes = null)
    {
        if (sourceEnvironmentId == targetEnvironmentId)
            throw new DomainException("RELATION_SELF_LOOP", "An environment cannot be related to itself.");

        return new EnvironmentRelation
        {
            CustomerId = customerId,
            SourceEnvironmentId = sourceEnvironmentId,
            TargetEnvironmentId = targetEnvironmentId,
            RelationType = relationType,
            Direction = direction,
            Notes = notes?.Trim()
        };
    }

    public void Update(RelationType type, RelationDirection direction, string? notes)
    {
        RelationType = type;
        Direction = direction;
        Notes = notes?.Trim();
        MarkUpdated();
    }

    public void AddTag(Tag tag)
    {
        if (!_tags.Contains(tag)) _tags.Add(tag);
    }
}
