namespace Clarity.Core;

public enum AssessmentKind { SsoDependency }
public enum SsoDependencyValue { Unknown, Required, NotRequired }
public enum SsoProtocol { Unknown, Saml, OpenIdConnect, Password, Other }

public sealed class Assessment : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid ObjectId { get; init; }
    public AssessmentKind Kind { get; init; }
    public long Revision { get; init; }
    public SsoDependencyValue Value { get; init; }
    public SsoProtocol Protocol { get; init; }
    public required string Reason { get; init; }
    public string EvidenceReference { get; init; } = "";
    public Guid? EvidenceObservationId { get; init; }
    public required string MetadataFingerprint { get; init; }
    public Guid AuthorTenantId { get; init; }
    public Guid AuthorObjectId { get; init; }
    public DateTimeOffset RecordedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
