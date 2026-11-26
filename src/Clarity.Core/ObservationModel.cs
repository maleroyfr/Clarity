namespace Clarity.Core;

public interface IProjectAccess
{
    Task<Project> RequireAsync(Guid projectId, WorkspaceOperation operation, CancellationToken cancellationToken = default);
}

public sealed class InventoryObject : WorkspaceEntity
{
    public Guid EnvironmentId { get; init; }
    public required string Type { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? DisplayName { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? Smtp { get; set; }
    public string? SearchName { get; private set; }
    public string? Domain { get; set; }
    public string? Site { get; set; }
    public bool? Enabled { get; set; }
    public DateTimeOffset? LastObservedAtUtc { get; set; }
    public InventoryPresence Presence { get; set; }
    public Guid? PresenceSectionId { get; set; }
}

public enum InventoryPresence { Unknown, Observed, AbsentFromScope, Deleted }

public sealed class CurrentValue : WorkspaceEntity
{
    public Guid ObjectId { get; init; }
    public Guid ObservationId { get; init; }
    public required string Field { get; init; }
    public required string ValueJson { get; init; }
    public bool Conflicting { get; init; }
}

public sealed class ExternalKey : WorkspaceEntity
{
    public Guid EnvironmentId { get; init; }
    public Guid ObjectId { get; init; }
    public required string Namespace { get; init; }
    public required string Type { get; init; }
    public required string ExternalId { get; init; }
    public bool IsPrimary { get; init; } = true;
    public string? SearchId { get; private set; }
}

public sealed class AlternateKeyPeriod : WorkspaceEntity
{
    public Guid EnvironmentId { get; init; }
    public Guid ObjectId { get; init; }
    public Guid ObservationId { get; init; }
    public required string Namespace { get; init; }
    public required string Type { get; init; }
    public required string OriginalValue { get; init; }
    public required string NormalizedValue { get; init; }
    public DateTimeOffset ValidFromUtc { get; init; }
    public DateTimeOffset? ValidUntilUtc { get; set; }
}

public sealed class ObjectObservation : WorkspaceEntity
{
    public Guid ObjectId { get; init; }
    public Guid SectionRunId { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public required string AttributesJson { get; init; }
    public string? AlternativeKeysJson { get; init; }
    public string? UserPrincipalName { get; init; }
    public string? Smtp { get; init; }
    public string? Status { get; init; }
    public string? DisplayName { get; init; }
}

public sealed class RelationshipObservation : WorkspaceEntity
{
    public Guid SectionRunId { get; init; }
    public required string RelationType { get; init; }
    public required string FromJson { get; init; }
    public required string ToJson { get; init; }
    public required string PropertiesJson { get; init; }
    public required string IdentityHash { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
}
