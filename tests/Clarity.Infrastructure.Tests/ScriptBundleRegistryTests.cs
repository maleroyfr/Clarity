using System.IO.Compression;
using System.Text;
using Clarity.Contracts;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

public sealed class ScriptBundleRegistryTests
{
    private static ScriptBundleRegistry Registry(params string[] resources) =>
        new(resources, name => new MemoryStream(Encoding.UTF8.GetBytes("content of " + name)));

    private static string[] Entries(byte[] bundle)
    {
        using var zip = new ZipArchive(new MemoryStream(bundle));
        return zip.Entries.Select(x => x.FullName).ToArray();
    }

    [Fact]
    public void A_family_is_a_folder_with_exactly_one_entry_script_and_the_common_module_is_appended_in_a_stable_order()
    {
        var registry = Registry(
            "Scripts/Clarity.Discovery.Common/Common.psm1", "Scripts/Clarity.Discovery.Common/Common.psd1",
            "Scripts/zz/b.psm1", "Scripts/zz/Collect-Zz.ps1", "Scripts/zz/a.psd1",
            @"Scripts\win\Collect-Win.ps1",
            "Scripts/two/Collect-One.ps1", "Scripts/two/Collect-Two.ps1",
            "Scripts/none/module.psm1",
            "Scripts/Upper/Collect-Upper.ps1", "Scripts/odd/Collect-Bad_Name.ps1", "Scripts/odd2/sub/Collect-Deep.ps1",
            "Scripts/Collect-Root.ps1", "Other/zz/Collect-Zz.ps1");

        registry.ProducerIds.ShouldBe(["clarity.win", "clarity.zz"]);
        registry.Supports("clarity.zz").ShouldBeTrue();
        registry.Supports("clarity.two").ShouldBeFalse();
        registry.Supports("clarity.Clarity.Discovery.Common").ShouldBeFalse();
        registry.Command("clarity.zz").ShouldBe(@".\zz\Collect-Zz.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip");
        registry.Command("clarity.unknown").ShouldBeNull();

        var bundle = registry.Create("clarity.zz")!;
        bundle.ShouldBe(registry.Create("clarity.zz")!);
        Entries(bundle).ShouldBe(["zz/Collect-Zz.ps1", "zz/a.psd1", "zz/b.psm1",
            "Clarity.Discovery.Common/Common.psd1", "Clarity.Discovery.Common/Common.psm1"]);
        using (var zip = new ZipArchive(new MemoryStream(bundle)))
        {
            new StreamReader(zip.GetEntry("zz/a.psd1")!.Open()).ReadToEnd().ShouldBe("content of Scripts/zz/a.psd1");
            zip.Entries.ShouldAllBe(x => x.LastWriteTime.DateTime == new DateTime(2000, 1, 1, 0, 0, 0));
        }

        var published = registry.Build("clarity.zz", "2.3.4")!;
        published.FileName.ShouldBe("Clarity.Discovery.Zz.2.3.4.zip");
        published.Content.ShouldBe(bundle);
        registry.Build("clarity.unknown", "1.0.0").ShouldBeNull();
        registry.Create("clarity.unknown").ShouldBeNull();
    }

    [Fact]
    public void Every_published_script_folder_is_the_bundle_of_a_validated_powershell_producer_of_the_catalog()
    {
        var catalog = DiscoveryCatalog.Load();
        ScriptBundle.Registry.ProducerIds.ShouldContain("clarity.ad");
        foreach (var id in ScriptBundle.Registry.ProducerIds)
        {
            var producer = catalog.Producers[id];
            (producer.Channel, producer.Readiness, producer.Preview).ShouldBe(("PowerShell", "SyntheticValidated", false), id);
            ScriptBundle.Registry.Command(id).ShouldNotBeNull();
            Entries(ScriptBundle.Registry.Create(id)!).ShouldContain("Clarity.Discovery.Common/Clarity.Discovery.Common.psm1", id);
        }
    }

    private static CatalogProducer Producer(string id, string channel = "PowerShell", string readiness = "SyntheticValidated", bool preview = false) =>
        new(id, channel, "1.0.0", "v1", preview, readiness, readiness == "Planned" ? [] : ["Synthetic test fixture only"], ["Prerequisite"], ["Limit"]);

    private static CatalogSection Section(string id, params string[] producers) =>
        new(id, producers, ["domain"], new Dictionary<string, CatalogField>(), [], ["Right"], ["Cmdlet"], "Full", ["Limit"]);

    private static (DiscoveryCatalog Catalog, ScriptBundleRegistry Registry) Fixture() => (
        new DiscoveryCatalog([
            new("1.0", "aa", "1.0.0", "AdForest", [Producer("clarity.aa")], [Section("aa.items", "clarity.aa")]),
            new("1.0", "bb", "1.0.0", "AdForest", [Producer("clarity.bb")], [Section("bb.items", "clarity.bb")]),
            new("1.0", "cc", "1.0.0", "AdForest",
                [Producer("clarity.cc", readiness: "Planned"), Producer("clarity.ccp", preview: true), Producer("clarity.ccg", "Graph"), Producer("clarity.ccn")],
                [Section("cc.items", "clarity.cc", "clarity.ccp", "clarity.ccg", "clarity.ccn")]),
            new("1.0", "dd", "1.0.0", "M365Tenant", [Producer("clarity.dd")], [Section("dd.items", "clarity.dd")]),
            new("1.0", "ee", "1.0.0", "AdForest", [Producer("clarity.ee1"), Producer("clarity.ee2")],
                [Section("ee.shared", "clarity.ee1", "clarity.ee2"), Section("ee.first", "clarity.ee1")])]),
        Registry(
            "Scripts/Clarity.Discovery.Common/Common.psm1",
            "Scripts/aa/Collect-Aa.ps1", "Scripts/bb/Collect-Bb.ps1", "Scripts/cc/Collect-Cc.ps1", "Scripts/ccp/Collect-Ccp.ps1",
            "Scripts/ccg/Collect-Ccg.ps1", "Scripts/dd/Collect-Dd.ps1", "Scripts/ee1/Collect-Ee1.ps1", "Scripts/ee2/Collect-Ee2.ps1"));

    [Fact]
    public void Only_validated_non_preview_powershell_producers_with_a_bundle_are_offered_for_their_environment_kind()
    {
        var (catalog, registry) = Fixture();

        registry.Producers(catalog, "AdForest").Select(x => x.Id).ShouldBe(["clarity.aa", "clarity.bb", "clarity.ee1", "clarity.ee2"]);
        registry.Producers(catalog, "M365Tenant").Select(x => x.Id).ShouldBe(["clarity.dd"]);
        registry.Producers(catalog, "AzureScope").ShouldBeEmpty();
    }

    [Fact]
    public void The_producer_is_inferred_from_the_sections_and_families_cannot_be_mixed()
    {
        var (catalog, registry) = Fixture();

        registry.Select(catalog, "AdForest", ["aa.items"]).Id.ShouldBe("clarity.aa");
        registry.Select(catalog, "AdForest", ["bb.items"]).Id.ShouldBe("clarity.bb");
        registry.Select(catalog, "M365Tenant", ["dd.items"]).Id.ShouldBe("clarity.dd");
        registry.Select(catalog, "AdForest", ["ee.first"]).Id.ShouldBe("clarity.ee1");
        string[][] invalid = [["aa.items", "bb.items"], [], ["unknown.items"], ["cc.items"], ["ee.shared"], ["aa.items", "unknown.items"]];
        foreach (var sections in invalid)
        {
            Should.Throw<ArgumentException>(() => registry.Select(catalog, "AdForest", sections), string.Join(",", sections));
        }
        Should.Throw<ArgumentException>(() => registry.Select(catalog, "AdForest", ["dd.items"]));
        Should.Throw<ArgumentException>(() => registry.Select(catalog, "M365Tenant", ["aa.items"]));
    }
}
