using System.Text.Json;
using Json.Schema;

namespace Clarity.Contracts;

public sealed class PackageValidator
{
    private static readonly IReadOnlyDictionary<string, JsonSchema> Schemas = LoadSchemas();

    public T Read<T>(string json, string file, int? line = null)
    {
        var schemaName = file switch
        {
            "manifest.json" => "manifest",
            "objects.jsonl" => "objects",
            "relationships.jsonl" => "relationships",
            "results.json" => "results",
            _ => throw new ContractException("UnknownFile", file)
        };
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicateProperties(document.RootElement, file, line);
            if (!Schemas[schemaName].Evaluate(document.RootElement,
                    new EvaluationOptions { RequireFormatValidation = true }).IsValid)
            {
                throw new ContractException("Schema", file, line);
            }
            return JsonSerializer.Deserialize<T>(json, ContractJson.Options)
                ?? throw new ContractException("NullDocument", file, line);
        }
        catch (JsonException)
        {
            throw new ContractException("Json", file, line);
        }
    }

    public void Validate(DiscoveryPackage package)
    {
        Read<PackageManifest>(JsonSerializer.Serialize(package.Manifest, ContractJson.Options), "manifest.json");
        Read<SectionResult[]>(JsonSerializer.Serialize(package.Results, ContractJson.Options), "results.json");
        foreach (var item in package.Objects)
        {
            Read<ObjectRecord>(JsonSerializer.Serialize(item, ContractJson.Options), "objects.jsonl");
        }
        foreach (var item in package.Relationships)
        {
            Read<RelationshipRecord>(JsonSerializer.Serialize(item, ContractJson.Options), "relationships.jsonl");
        }
        ValidateReferences(package);
    }

    public static void ValidateReferences(DiscoveryPackage package)
    {
        var manifest = package.Manifest;
        if (manifest.RunId == Guid.Empty || manifest.RequestId == Guid.Empty
            || manifest.CompletedAtUtc < manifest.StartedAtUtc)
        {
            throw new ContractException("RunIdentityOrDates", "manifest.json");
        }

        var sections = new Dictionary<string, SectionResult>(StringComparer.Ordinal);
        var scopes = new HashSet<(string, CollectionScope)>();
        foreach (var section in package.Results)
        {
            if (!sections.TryAdd(section.SectionId, section)
                || !scopes.Add((section.CapabilityId, section.Scope)))
            {
                throw new ContractException("DuplicateSectionScope", "results.json");
            }
            if (section.Status == CoverageStatus.Complete && section.Errors.Count != 0)
            {
                throw new ContractException("CompleteWithErrors", "results.json");
            }
            if (section.Status is CoverageStatus.Partial or CoverageStatus.Failed
                && section.Errors.Count == 0 && section.Warnings.Count == 0)
            {
                throw new ContractException("MissingFailureReason", "results.json");
            }
        }

        var objectKeys = new HashSet<(string, string, string, string)>();
        foreach (var item in package.Objects)
        {
            var section = GetSection(item.SectionId, "objects.jsonl");
            if (item.Attributes.Keys.Except(section.CoveredFields, StringComparer.Ordinal).Any()
                || !objectKeys.Add((item.SectionId, item.Namespace, item.Type, item.ExternalId)))
            {
                throw new ContractException("ObjectCoverageOrDuplicate", "objects.jsonl");
            }
            ValidateDate(item.ObservedAtUtc, "objects.jsonl");
        }
        var relationshipKeys = new HashSet<(string, string, ExternalReference, ExternalReference)>();
        foreach (var item in package.Relationships)
        {
            var section = GetSection(item.SectionId, "relationships.jsonl");
            if (!section.CoveredRelations.Contains(item.RelationType, StringComparer.Ordinal)
                || !relationshipKeys.Add((item.SectionId, item.RelationType, item.From, item.To)))
            {
                throw new ContractException("RelationshipCoverageOrDuplicate", "relationships.jsonl");
            }
            ValidateDate(item.ObservedAtUtc, "relationships.jsonl");
        }
        var objectCounts = package.Objects.CountBy(x => x.SectionId).ToDictionary(StringComparer.Ordinal);
        var relationshipCounts = package.Relationships.CountBy(x => x.SectionId).ToDictionary(StringComparer.Ordinal);
        foreach (var section in package.Results)
        {
            if (section.Status is CoverageStatus.Complete or CoverageStatus.Partial
                && (section.ObjectCount != objectCounts.GetValueOrDefault(section.SectionId)
                    || section.RelationshipCount != relationshipCounts.GetValueOrDefault(section.SectionId)))
            {
                throw new ContractException("Counters", "results.json");
            }
        }
        return;

        SectionResult GetSection(string id, string file)
        {
            if (!sections.TryGetValue(id, out var section)
                || section.Status is CoverageStatus.Failed or CoverageStatus.NotApplicable)
            {
                throw new ContractException("SectionReference", file);
            }
            return section;
        }

        void ValidateDate(DateTimeOffset date, string file)
        {
            if (date < manifest.StartedAtUtc || date > manifest.CompletedAtUtc)
            {
                throw new ContractException("ObservationDate", file);
            }
        }
    }

    private static void RejectDuplicateProperties(JsonElement element, string file, int? line)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new ContractException("DuplicateProperty", file, line);
                }
                RejectDuplicateProperties(property.Value, file, line);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                RejectDuplicateProperties(child, file, line);
            }
        }
    }

    private static Dictionary<string, JsonSchema> LoadSchemas()
    {
        var assembly = typeof(PackageValidator).Assembly;
        var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
        foreach (var name in new[] { "manifest", "objects", "relationships", "results" })
        {
            using var stream = assembly.GetManifestResourceStream($"Clarity.Contracts.Schemas.{name}.schema.json")
                ?? throw new InvalidOperationException($"Missing embedded {name} schema.");
            using var document = JsonDocument.Parse(stream);
            schemas.Add(name, JsonSchema.Build(document.RootElement.Clone(),
                new BuildOptions { Dialect = Dialect.Draft202012, SchemaRegistry = new() }));
        }
        return schemas;
    }
}
