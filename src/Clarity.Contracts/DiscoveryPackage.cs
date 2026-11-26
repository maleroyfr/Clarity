using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clarity.Contracts;

public enum CoverageStatus { Complete, Partial, Failed, NotApplicable }

public sealed record CollectionScope(string Kind, string Value);

public sealed record PackageManifest(
    string SchemaVersion,
    Guid RunId,
    string ProducerId,
    string ProducerVersion,
    string EnvironmentKind,
    string EnvironmentExternalId,
    CollectionScope Scope,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyDictionary<string, string> ModuleVersions,
    string SourceId,
    Guid? RequestId = null);

public sealed record ExternalReference(string Type, string Namespace, string ExternalId);

public sealed record ObjectRecord(
    string SectionId,
    string Type,
    string Namespace,
    string ExternalId,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyDictionary<string, JsonElement> Attributes,
    IReadOnlyList<ExternalReference>? AlternativeKeys = null);

public sealed record RelationshipRecord(
    string SectionId,
    string RelationType,
    ExternalReference From,
    ExternalReference To,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyDictionary<string, JsonElement>? Properties = null);

public sealed record SectionIssue(string Code, string Message);

public sealed record SectionResult(
    string SectionId,
    string CapabilityId,
    CoverageStatus Status,
    CollectionScope Scope,
    IReadOnlyList<string> CoveredFields,
    IReadOnlyList<string> CoveredRelations,
    long? ObjectCount,
    long? RelationshipCount,
    IReadOnlyList<SectionIssue> Warnings,
    IReadOnlyList<SectionIssue> Errors);

public sealed record DiscoveryPackage(
    PackageManifest Manifest,
    IReadOnlyList<ObjectRecord> Objects,
    IReadOnlyList<RelationshipRecord> Relationships,
    IReadOnlyList<SectionResult> Results);

public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

public sealed class ContractException(string code, string file, int? line = null, IReadOnlyList<string>? details = null)
    : Exception($"Invalid Discovery package: {code} ({file}{(line is null ? "" : $":{line}")}).")
{
    public string Code { get; } = code;
    public string File { get; } = file;
    public int? Line { get; } = line;
    /// <summary>Safe, consultant-facing hints such as profile column names; never cell values or storage paths.</summary>
    public IReadOnlyList<string> Details { get; } = details ?? [];
}
