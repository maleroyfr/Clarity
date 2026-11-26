using Clarity.Contracts;
using Shouldly;

namespace Clarity.Contracts.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void LoadsCatalogWithUniqueStableKeysAndUnavailableFutureProducers()
    {
        var catalog = DiscoveryCatalog.Load();
        catalog.Sections.ShouldContainKey("ad.groups");
        catalog.Sections.ShouldContainKey("entra.users");
        Should.Throw<ContractException>(() => catalog.RequireProducer("clarity.exchange"));
        catalog.Producers["clarity.exchange"].TestedVersions.ShouldBeEmpty();
        catalog.Producers["clarity.graph.entra"].ApiVersion.ShouldBe("v1.0");
    }

    [Fact]
    public void RejectsDuplicateSectionsAndDanglingFieldReferences()
    {
        var family = DiscoveryCatalog.Load().Families.First();
        Should.Throw<InvalidOperationException>(() => new DiscoveryCatalog([family, family]));
        Should.Throw<InvalidOperationException>(() => new DiscoveryCatalog([family with
        {
            Sections = [family.Sections[0] with
            {
                Attributes = new Dictionary<string, CatalogField> { ["name"] = new("string", "unknown") }
            }]
        }]));
    }

    [Fact]
    public void PreviewRequiresExplicitOptInAndTestedEvidence()
    {
        var family = DiscoveryCatalog.Load().Families.First();
        var producer = family.Producers[0] with
        {
            ApiVersion = "beta",
            Preview = true,
            Readiness = "SyntheticValidated",
            TestedVersions = ["Synthetic test only"]
        };
        var catalog = new DiscoveryCatalog([family with { Producers = [producer] }]);
        Should.Throw<ContractException>(() => catalog.RequireProducer(producer.Id));
        catalog.RequireProducer(producer.Id, allowPreview: true).ShouldBe(producer);
        Should.Throw<InvalidOperationException>(() => new DiscoveryCatalog([family with
        {
            Producers = [producer with { Preview = false }]
        }]));
        Should.Throw<InvalidOperationException>(() => new DiscoveryCatalog([family with
        {
            Producers = [producer with { TestedVersions = [] }]
        }]));
    }
}
