namespace Clarity.Core;

public enum EndpointResolution { Pending, Primary, Alternate, Ambiguous }
public enum RelationshipOrigin { Observed, Membership, ObservedDelegation, ConsultantQualification }

public sealed class InventoryRelationship : WorkspaceEntity
{
    public Guid EnvironmentId { get; init; }
    public Guid? FromObjectId { get; init; }
    public Guid? ToObjectId { get; init; }
    public required string FromJson { get; init; }
    public required string ToJson { get; init; }
    public required string RelationType { get; init; }
    public EndpointResolution FromMethod { get; init; }
    public EndpointResolution ToMethod { get; init; }
    public RelationshipOrigin Origin { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed class RelationshipSupport : WorkspaceEntity
{
    public Guid RelationshipId { get; init; }
    public Guid ObservationId { get; init; }
}
