namespace Clarity.Core;

public enum SourceAnchor { ObjectGuid, ConsistencyGuid }
public enum MappingState { Proposed, Validated, Rejected, ToReview }
public enum AssociationRule
{
    ConfiguredAnchor, SecurityIdentifier, ExactUpn, ExactSmtp, DeviceIdentifier, DeviceHint, NoCurrentCandidate, ConsultantQualified,
    MappingRuleAttribute, ComputedTargetUpn
}

public sealed class SyncBinding : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid AdEnvironmentId { get; init; }
    public Guid EntraEnvironmentId { get; init; }
    public SourceAnchor? Anchor { get; set; }
    public Guid VerifiedByTenantId { get; set; }
    public Guid VerifiedByObjectId { get; set; }
    public DateTimeOffset VerifiedAtUtc { get; set; }
    public required string Reason { get; set; }
}

public sealed class MappingDecision : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public MappingKind Kind { get; init; }
    public Guid AdEnvironmentId { get; init; }
    public Guid EntraEnvironmentId { get; init; }
    public Guid AdObjectId { get; init; }
    public Guid EntraObjectId { get; init; }
    public MappingState State { get; set; }
    public AssociationRule Rule { get; set; }
    public required string Fingerprint { get; set; }
    public required string EvidenceJson { get; set; }
    public long Revision { get; set; }
    public bool ConsultantQualified { get; set; }
}

public sealed class MappingRevision : WorkspaceEntity
{
    public Guid DecisionId { get; init; }
    public long Number { get; init; }
    public MappingState State { get; init; }
    public AssociationRule Rule { get; init; }
    public required string EvidenceJson { get; init; }
    public required string Reason { get; init; }
    public Guid? ActorTenantId { get; init; }
    public Guid? ActorObjectId { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
