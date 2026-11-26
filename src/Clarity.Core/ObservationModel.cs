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
}

public sealed class ExternalKey : WorkspaceEntity
{
    public Guid EnvironmentId { get; init; }
    public Guid ObjectId { get; init; }
    public required string Namespace { get; init; }
    public required string Type { get; init; }
    public required string ExternalId { get; init; }
    public bool IsPrimary { get; init; } = true;
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
