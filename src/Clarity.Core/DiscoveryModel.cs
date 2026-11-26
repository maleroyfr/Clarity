using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clarity.Contracts;

namespace Clarity.Core;

public enum CollectionRequestState { AwaitingFile, FileToConfirm, Integrated }
public enum CollectionRunState { Pending, Running, Completed, Failed, Cancelled }
public enum CollectionChannel { Graph, PowerShell, Report }

public sealed class Connection : WorkspaceEntity
{
    public required string Name { get; set; }
    public Guid TenantId { get; init; }
    public required string ProducerId { get; init; }
    public required string ConfigurationReference { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TestedAtUtc { get; set; }
    public string AccessResultsJson { get; set; } = "[]";
}

public sealed class CollectionRequest : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public required string ScopeJson { get; init; }
    public required string ScopeHash { get; init; }
    public required string SectionsJson { get; init; }
    public required string ProducerId { get; init; }
    public required string ScriptVersion { get; init; }
    public string ParametersJson { get; init; } = "{}";
    public CollectionRequestState State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public Guid ActorTenantId { get; init; }
    public Guid ActorObjectId { get; init; }
}

public sealed class CollectionRun : WorkspaceEntity
{
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid ExternalRunId { get; init; }
    public Guid? RequestId { get; init; }
    public Guid? ConnectionId { get; init; }
    public CollectionChannel Channel { get; init; }
    public required string ProducerId { get; init; }
    public required string ProducerVersion { get; init; }
    public required string ScopeJson { get; init; }
    public required string ScopeHash { get; init; }
    public required string ManifestJson { get; init; }
    public string ContinuationJson { get; init; } = "[]";
    public required string ContentHash { get; init; }
    public CollectionRunState State { get; set; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public DateTimeOffset UploadedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public Guid ActorTenantId { get; init; }
    public Guid ActorObjectId { get; init; }
    public required string CorrelationId { get; init; }
}

public sealed class SectionRun : WorkspaceEntity
{
    public Guid RunId { get; init; }
    public required string SectionId { get; init; }
    public required string CapabilityId { get; init; }
    public required string ScopeJson { get; init; }
    public required string ScopeHash { get; init; }
    public CoverageStatus Coverage { get; init; }
    public required string ResultJson { get; init; }
    public long? ObjectCount { get; init; }
    public long? RelationshipCount { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}

public sealed class RawArtifact : WorkspaceEntity
{
    public Guid RunId { get; init; }
    public required string BlobName { get; init; }
    public required string ContentHash { get; init; }
    public required string FileName { get; init; }
    public long CompressedBytes { get; init; }
    public long UncompressedBytes { get; init; }
    public DateTimeOffset UploadedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public static class DiscoveryScope
{
    public static string Serialize(CollectionScope scope) => JsonSerializer.Serialize(scope, ContractJson.Options);
    public static string Hash(CollectionScope scope) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(scope))));

    public static void Validate(string json, string hash)
    {
        var scope = JsonSerializer.Deserialize<CollectionScope>(json, ContractJson.Options);
        if (scope is null || string.IsNullOrWhiteSpace(scope.Value) || scope.Value.Length > 2000
            || scope.Kind is not ("tenant" or "forest" or "domain" or "ou" or "resource")
            || hash != Hash(scope))
        {
            throw new ArgumentException("Invalid collection scope.");
        }
    }
}
