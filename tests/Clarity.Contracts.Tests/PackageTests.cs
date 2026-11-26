using System.Text.Json;
using Clarity.Contracts;
using Shouldly;

namespace Clarity.Contracts.Tests;

public sealed class PackageTests
{
    private readonly PackageValidator validator = new();

    [Fact]
    public void PartialExamplePreservesExplicitEmptyValuesAndUnresolvedReferences()
    {
        var package = Example();
        validator.Validate(package);
        package.Objects[0].Attributes.ContainsKey("userPrincipalName").ShouldBeFalse();
        package.Objects[0].Attributes["mail"].ValueKind.ShouldBe(JsonValueKind.Null);
        package.Objects[0].Attributes["proxyAddresses"].GetArrayLength().ShouldBe(0);
        package.Results[1].Status.ShouldBe(CoverageStatus.Partial);
    }

    [Theory]
    [InlineData("\"schemaVersion\": \"1.0\"", "\"schemaVersion\": \"2.0\"")]
    [InlineData("\"environmentExternalId\": \"forest.example.test\"", "\"environmentExternalId\": 123")]
    [InlineData("2026-01-01T10:00:00Z", "2026-01-01T10:00:00+01:00")]
    [InlineData("2026-01-01T10:00:00Z", "2026-99-01T10:00:00Z")]
    [InlineData("\"runId\":", "\"schemaVersion\":\"1.0\",\"runId\":")]
    public void RejectsMalformedManifest(string before, string after)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", "manifest.json"));
        Should.Throw<ContractException>(() => validator.Read<PackageManifest>(json.Replace(before, after), "manifest.json"));
    }

    [Fact]
    public void RejectsInconsistentSectionsAndCounters()
    {
        var package = Example();
        Should.Throw<ContractException>(() => validator.Validate(package with
        {
            Results = [package.Results[0], package.Results[0]]
        })).Code.ShouldBe("DuplicateSectionScope");
        Should.Throw<ContractException>(() => validator.Validate(package with
        {
            Objects = [package.Objects[0] with { SectionId = "missing" }]
        })).Code.ShouldBe("SectionReference");
        Should.Throw<ContractException>(() => validator.Validate(package with
        {
            Objects = []
        })).Code.ShouldBe("Counters");
        Should.Throw<ContractException>(() => validator.Validate(package with
        {
            Results = [package.Results[0], package.Results[1] with { Status = CoverageStatus.Failed }]
        })).Code.ShouldBe("SectionReference");
    }

    [Fact]
    public void CompleteZeroIsDistinctFromUnknownAndNotApplicable()
    {
        var package = Example() with { Objects = [], Relationships = [] };
        validator.Validate(package with
        {
            Results = [package.Results[0] with { ObjectCount = 0, RelationshipCount = 0 }]
        });
        validator.Validate(package with
        {
            Results = [package.Results[0] with { Status = CoverageStatus.NotApplicable, ObjectCount = null, RelationshipCount = null }]
        });
        Should.Throw<ContractException>(() => validator.Validate(package with
        {
            Results = [package.Results[0] with { ObjectCount = null }]
        }));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"12 MB\"")]
    public void VolumesUseNonnegativeIntegerBytes(string value)
    {
        var json = """{"sectionId":"x","type":"site","namespace":"graph","externalId":"1","observedAtUtc":"2026-01-01T00:00:00Z","attributes":{"sizeBytes":VALUE}}"""
            .Replace("VALUE", value);
        Should.Throw<ContractException>(() => validator.Read<ObjectRecord>(json, "objects.jsonl"));
    }

    private DiscoveryPackage Example()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Examples");
        return new(
            validator.Read<PackageManifest>(File.ReadAllText(Path.Combine(path, "manifest.json")), "manifest.json"),
            File.ReadAllLines(Path.Combine(path, "objects.jsonl")).Select(x => validator.Read<ObjectRecord>(x, "objects.jsonl")).ToArray(),
            File.ReadAllLines(Path.Combine(path, "relationships.jsonl")).Select(x => validator.Read<RelationshipRecord>(x, "relationships.jsonl")).ToArray(),
            validator.Read<SectionResult[]>(File.ReadAllText(Path.Combine(path, "results.json")), "results.json"));
    }
}
