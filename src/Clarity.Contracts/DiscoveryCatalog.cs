using System.Globalization;
using System.Text.Json;
using Json.Schema;

namespace Clarity.Contracts;

public sealed record CatalogField(string Type, string ReferenceProducer);
public sealed record CatalogProducer(
    string Id, string Channel, string Version, string ApiVersion, bool Preview,
    string Readiness, IReadOnlyList<string> TestedVersions,
    IReadOnlyList<string> Prerequisites, IReadOnlyList<string> Limits, bool VisibilityLimited = false);
public sealed record CatalogSection(
    string Id, IReadOnlyList<string> ProducerIds, IReadOnlyList<string> SupportedScopes,
    IReadOnlyDictionary<string, CatalogField> Attributes, IReadOnlyList<string> Relations,
    IReadOnlyList<string> Permissions, IReadOnlyList<string> Endpoints,
    string RefreshStrategy, IReadOnlyList<string> Limits);
public sealed record CatalogFamily(
    string SchemaVersion, string Id, string Version, string EnvironmentKind,
    IReadOnlyList<CatalogProducer> Producers, IReadOnlyList<CatalogSection> Sections);

public sealed class DiscoveryCatalog
{
    public IReadOnlyList<CatalogFamily> Families { get; }
    public IReadOnlyDictionary<string, CatalogSection> Sections { get; }
    public IReadOnlyDictionary<string, CatalogProducer> Producers { get; }

    public DiscoveryCatalog(IEnumerable<CatalogFamily> families)
    {
        Families = families.ToArray();
        var sections = new Dictionary<string, CatalogSection>(StringComparer.Ordinal);
        var producers = new Dictionary<string, CatalogProducer>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in Families)
        {
            ValidateShape(family);
            if (!ids.Add(family.Id))
            {
                throw new InvalidOperationException("Duplicate catalog family.");
            }
            foreach (var producer in family.Producers)
            {
                if (!producers.TryAdd(producer.Id, producer)
                    || (producer.ApiVersion == "beta" && !producer.Preview)
                    || (producer.Readiness == "SyntheticValidated" && producer.TestedVersions.Count == 0))
                {
                    throw new InvalidOperationException("Invalid catalog producer.");
                }
            }
            var familyProducers = family.Producers.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var section in family.Sections)
            {
                if (!sections.TryAdd(section.Id, section)
                    || !section.Id.StartsWith(family.Id + ".", StringComparison.Ordinal)
                    || section.ProducerIds.Any(x => !familyProducers.Contains(x))
                    || section.Attributes.Values.Any(x => !section.ProducerIds.Contains(x.ReferenceProducer, StringComparer.Ordinal)))
                {
                    throw new InvalidOperationException("Invalid catalog section or field reference producer.");
                }
            }
        }
        Sections = sections;
        Producers = producers;
    }

    public CatalogProducer RequireProducer(string id, bool allowPreview = false)
    {
        if (!Producers.TryGetValue(id, out var producer)
            || producer.Readiness != "SyntheticValidated" || (producer.Preview && !allowPreview))
        {
            throw new ContractException("ProducerUnavailable", "catalog");
        }
        return producer;
    }

    public void ValidatePackage(DiscoveryPackage package, bool allowPreview = false)
    {
        var producer = RequireProducer(package.Manifest.ProducerId, allowPreview);
        var family = Families.Single(x => x.Producers.Contains(producer));
        if (package.Manifest.ProducerVersion != producer.Version
            || package.Manifest.EnvironmentKind != family.EnvironmentKind)
        {
            throw new ContractException("ProducerVersionOrEnvironment", "manifest.json");
        }
        foreach (var result in package.Results)
        {
            if (producer.VisibilityLimited && result.Status == CoverageStatus.Complete
                || !Sections.TryGetValue(result.CapabilityId, out var section)
                || !section.ProducerIds.Contains(producer.Id, StringComparer.Ordinal)
                || !section.SupportedScopes.Contains(result.Scope.Kind, StringComparer.Ordinal)
                || result.CoveredFields.Except(section.Attributes.Keys, StringComparer.Ordinal).Any()
                || result.CoveredRelations.Except(section.Relations, StringComparer.Ordinal).Any())
            {
                throw new ContractException("CatalogCoverage", "results.json");
            }
            foreach (var item in package.Objects.Where(x => x.SectionId == result.SectionId))
            {
                foreach (var (name, value) in item.Attributes)
                {
                    if (!section.Attributes.TryGetValue(name, out var field) || !Accepts(field.Type, value))
                    {
                        throw new ContractException("AttributeType", "objects.jsonl");
                    }
                }
            }
        }
    }

    public static DiscoveryCatalog Load()
    {
        var assembly = typeof(DiscoveryCatalog).Assembly;
        return new DiscoveryCatalog(assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith("Clarity.Contracts.Catalog.", StringComparison.Ordinal)
                && x.EndsWith(".v1.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                return JsonSerializer.Deserialize<CatalogFamily>(stream, ContractJson.Options)
                    ?? throw new InvalidOperationException("Empty catalog family.");
            }));
    }

    private static bool Accepts(string type, JsonElement value) =>
        value.ValueKind == JsonValueKind.Null || type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "bytes" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var bytes) && bytes >= 0,
            "strings" => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String),
            "utc" => value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var date) && date.Offset == TimeSpan.Zero,
            "date" => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => false
        };

    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var stream = typeof(DiscoveryCatalog).Assembly
            .GetManifestResourceStream("Clarity.Contracts.Catalog.family.schema.json")!;
        using var document = JsonDocument.Parse(stream);
        return JsonSchema.Build(document.RootElement.Clone(),
            new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = new() });
    });

    private static void ValidateShape(CatalogFamily family)
    {
        var json = JsonSerializer.SerializeToElement(family, ContractJson.Options);
        if (!Schema.Value.Evaluate(json).IsValid)
        {
            throw new InvalidOperationException("Invalid catalog family structure.");
        }
    }
}
