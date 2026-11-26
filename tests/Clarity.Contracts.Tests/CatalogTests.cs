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
        foreach (var planned in catalog.Producers.Values.Where(x => x.Readiness == "Planned"))
        {
            Should.Throw<ContractException>(() => catalog.RequireProducer(planned.Id));
            planned.TestedVersions.ShouldBeEmpty();
        }
        catalog.Producers["clarity.graph.entra"].ApiVersion.ShouldBe("v1.0");
    }

    [Fact]
    public void ExchangeProducerIsSyntheticValidatedWithStableSections()
    {
        var catalog = DiscoveryCatalog.Load();
        var producer = catalog.RequireProducer("clarity.exchange");
        producer.Version.ShouldBe("1.0.0");
        producer.TestedVersions.ShouldNotBeEmpty();
        producer.Prerequisites.ShouldContain(x => x.Contains("ExchangeOnlineManagement 3.0.0", StringComparison.Ordinal));
        var sections = catalog.Sections.Values.Where(x => x.ProducerIds.Contains("clarity.exchange")).ToArray();
        sections.Select(x => x.Id).Order(StringComparer.Ordinal).ShouldBe(
        [
            "exchange.acceptedDomains", "exchange.connectors", "exchange.mailEnabledGroups", "exchange.mailboxPermissions",
            "exchange.mailboxStatistics", "exchange.mailboxes", "exchange.recipients", "exchange.transportRules"
        ]);
        sections.ShouldAllBe(x => x.SupportedScopes.SequenceEqual(new[] { "tenant" }) && x.Endpoints.Count > 0
            && x.Permissions.Count > 0 && x.Limits.Count > 0);
        catalog.Sections["exchange.mailboxPermissions"].Relations.ShouldBe(["fullAccessTo", "sendAsOn", "sendOnBehalfOf"]);
        catalog.Sections["exchange.mailboxes"].Relations.ShouldBe(["mailboxOf"]);
        catalog.Sections["exchange.mailboxStatistics"].Attributes["sizeBytes"].Type.ShouldBe("bytes");
        catalog.Sections.ShouldContainKey("exchange.mailboxUsage");
        catalog.Sections["exchange.mailboxUsage"].ProducerIds.ShouldBe(["report.m365.mailboxUsageDetail"]);
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
