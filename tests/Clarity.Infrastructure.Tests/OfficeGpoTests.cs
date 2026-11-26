using System.IO.Compression;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class OfficeGpoTests
{
    [Fact]
    public void OfficeGpoSectionsSelectTheirOwnBundleWithoutAdCollectorOrRequestParameters()
    {
        var catalog = DiscoveryCatalog.Load();
        var producer = ScriptBundle.Registry.Select(catalog, "AdForest", ["officeGpo.links", "officeGpo.officeSettings"]);
        producer.Id.ShouldBe("clarity.officegpo");
        Should.Throw<ArgumentException>(() => ScriptBundle.Registry.Select(catalog, "AdForest", ["officeGpo.links", "ad.ous"]));
        var bundle = ScriptBundle.Build(producer.Id, producer.Version).ShouldNotBeNull();
        bundle.FileName.ShouldBe("Clarity.Discovery.OfficeGpo.1.0.0.zip");
        ScriptBundle.Command(producer.Id).ShouldBe(@".\officegpo\Collect-OfficeGpo.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip");
        using var zip = new ZipArchive(new MemoryStream(bundle.Content));
        zip.Entries.Select(x => x.FullName).ShouldBe([
            "officegpo/Collect-OfficeGpo.ps1", "officegpo/Clarity.Discovery.OfficeGpo.psm1",
            "Clarity.Discovery.Common/Clarity.Discovery.Common.psd1", "Clarity.Discovery.Common/Clarity.Discovery.Common.psm1"]);
    }

    [Fact]
    public async Task GeneratedOfficeGpoPackagesPreserveTypedScopeCoverageAndAllowlistedSettings()
    {
        var package = await ReadFixtureAsync("complete");
        package.Manifest.ProducerId.ShouldBe("clarity.officegpo");
        package.Manifest.ModuleVersions["GroupPolicy"].ShouldBe("1.0.0.0");
        package.Results.ShouldAllBe(x => x.Status == CoverageStatus.Complete);
        var settings = package.Objects.Where(x => x.Type == "officePolicySetting").ToArray();
        settings.Length.ShouldBe(6);
        settings.ShouldAllBe(x => x.Attributes["configuration"].GetString() == "User");
        settings.Where(x => x.Attributes["policyState"].GetString() == "Delete")
            .ShouldAllBe(x => x.Attributes["integerValue"].ValueKind == JsonValueKind.Null);
        package.Relationships.Count(x => x.RelationType == "gpoLinkedToOu").ShouldBe(1);
        package.Relationships.Where(x => x.RelationType == "gpoSecurityFilter").Select(x => x.To.Type)
            .ShouldBe(["group", "computer", "directoryObject"]);
        var ou = await ReadFixtureAsync("ou");
        ou.Results.ShouldAllBe(x => x.Scope == ou.Manifest.Scope);
        ou.Manifest.Scope.ShouldBe(new("ou", "OU=Pilot,DC=example,DC=test"));
        var empty = await ReadFixtureAsync("empty");
        empty.Results.ShouldAllBe(x => x.Status == CoverageStatus.Complete && x.ObjectCount == 0 && x.RelationshipCount == 0);
        var failed = await ReadFixtureAsync("failed");
        failed.Results.ShouldAllBe(x => x.Status == CoverageStatus.Failed && x.ObjectCount == null && x.Errors.Count > 0);
        var partial = await ReadFixtureAsync("partial");
        partial.Results.Single(x => x.CapabilityId == "officeGpo.officeSettings").Status.ShouldBe(CoverageStatus.Partial);
    }

    [SqlFact]
    public async Task OfficeGpoPublicationResolvesLaterAdEvidenceWithoutInventingMembershipOrEffectivePolicy()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var package = await ReadFixtureAsync("complete");
        async Task Publish(DiscoveryPackage data)
        {
            var run = await fixture.StageAsync(data);
            foreach (var section in data.Results)
            {
                await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, run.Id, data, section.SectionId);
                await fixture.Publisher.PublishSectionAsync(fixture.Project.Id, run.Id, data, section.SectionId);
            }
        }
        var requests = new ScriptRequestService(fixture.Factory, fixture.Access, fixture.Scope, DiscoveryCatalog.Load());
        var requestId = await requests.CreateAsync(fixture.Project.Id, fixture.Environment.Id, package.Manifest.RequestId!.Value,
            package.Manifest.Scope, package.Results.Select(x => x.CapabilityId).ToArray(), domain: null);
        var parameters = JsonSerializer.Deserialize<ScriptParameters>(await requests.DownloadParametersAsync(fixture.Project.Id, requestId), ContractJson.Options)!;
        parameters.ExpectedEnvironmentExternalId.ShouldBe(package.Manifest.EnvironmentExternalId);
        parameters.Scope.ShouldBe(package.Manifest.Scope);
        parameters.Domain.ShouldBe("example.test");
        var bundle = await requests.DownloadBundleAsync(fixture.Project.Id, requestId);
        bundle.Content.ShouldBe(ScriptBundle.Build(package.Manifest.ProducerId, package.Manifest.ProducerVersion)!.Content);
        await Should.ThrowAsync<AccessDeniedException>(() => requests.DownloadBundleAsync(Guid.NewGuid(), requestId));
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        await using var resultZip = OpenFixture("complete");
        var runId = await imports.UploadAsync(fixture.Project.Id, fixture.Environment.Id, resultZip, "synthetic-office-gpo.zip");
        (await imports.PreviewAsync(fixture.Project.Id, runId)).NewObjects.ShouldBe(7);
        await imports.ConfirmAsync(fixture.Project.Id, runId);
        await ZipImportTests.Processor(fixture, store).ProcessOneAsync(default);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        (await db.CollectionRequests.SingleAsync()).State.ShouldBe(CollectionRequestState.Integrated);
        (await db.InventoryObjects.CountAsync()).ShouldBe(7);
        (await db.RelationshipObservations.CountAsync()).ShouldBe(11);
        (await db.InventoryRelationships.CountAsync(x => x.ToMethod == EndpointResolution.Pending)).ShouldBe(5);
        (await db.InventoryRelationships.CountAsync(x => x.Origin == RelationshipOrigin.Membership)).ShouldBe(0);
        var date = package.Manifest.CompletedAtUtc.AddMinutes(1);
        var scope = package.Manifest.Scope;
        ObjectRecord Ad(string section, string type, string id, string? sid = null) => new(
            section, type, "ad.objectGUID", id, date, sid is null
                ? new Dictionary<string, JsonElement> { ["displayName"] = JsonSerializer.SerializeToElement("Synthetic OU") }
                : new Dictionary<string, JsonElement> { ["objectSid"] = JsonSerializer.SerializeToElement(sid) });
        await Publish(new DiscoveryPackage(
            package.Manifest with { RunId = Guid.NewGuid(), RequestId = null, ProducerId = "clarity.ad", StartedAtUtc = date, CompletedAtUtc = date },
            [
                Ad("ad.ous:pilot", "organizationalUnit", "44444444-0000-0000-0000-000000000002"),
                Ad("ad.groups:pilot", "group", "44444444-0000-0000-0000-000000000005", "S-1-5-21-100-200-300-2001"),
                Ad("ad.computers:pilot", "computer", "44444444-0000-0000-0000-000000000006", "S-1-5-21-100-200-300-2002")
            ], [],
            [
                new("ad.ous:pilot", "ad.ous", CoverageStatus.Complete, scope, ["displayName"], [], 1, 0, [], []),
                new("ad.groups:pilot", "ad.groups", CoverageStatus.Complete, scope, ["objectSid"], [], 1, 0, [], []),
                new("ad.computers:pilot", "ad.computers", CoverageStatus.Complete, scope, ["objectSid"], [], 1, 0, [], [])
            ]));
        (await db.InventoryRelationships.SingleAsync(x => x.RelationType == "gpoLinkedToOu")).ToMethod.ShouldBe(EndpointResolution.Primary);
        (await db.InventoryRelationships.CountAsync(x => x.RelationType == "gpoSecurityFilter" && x.ToMethod == EndpointResolution.Alternate)).ShouldBe(2);
        (await db.InventoryRelationships.CountAsync(x => x.ToMethod == EndpointResolution.Pending)).ShouldBe(2);
        var gpoId = await db.ExternalKeys.Where(x => x.Namespace == "ad.gpoId").Select(x => x.ObjectId).SingleAsync();
        (await new InventoryGraph(fixture.Factory, fixture.Access).MembershipsAsync(fixture.Project.Id, gpoId, reverse: false)).ShouldBeEmpty();
        await Publish(await ReadFixtureAsync("failed"));
        (await db.InventoryObjects.CountAsync()).ShouldBe(10);
        (await db.InventoryRelationships.CountAsync(x => x.IsCurrent)).ShouldBe(11);
        (await db.RelationshipSupports.CountAsync()).ShouldBe(11);
        var other = await fixture.OtherWorkspaceAsync();
        await Should.ThrowAsync<AccessDeniedException>(() =>
            new InventoryGraph(other.Factory, other.Access).MembershipsAsync(fixture.Project.Id, gpoId, reverse: false));
        await using var outsider = PersistenceTests.Create(fixture.ConnectionString, [Guid.NewGuid()]);
        (await outsider.InventoryRelationships.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    private static FileStream OpenFixture(string name)
    {
        var directory = Environment.GetEnvironmentVariable("CLARITY_SCRIPT_FIXTURES");
        if (string.IsNullOrEmpty(directory)) { Assert.Skip("Set CLARITY_SCRIPT_FIXTURES and run Pester first; CI validates generated Office/GPO output."); }
        var path = Path.Combine(directory, $"office-gpo-{name}.zip");
        File.Exists(path).ShouldBeTrue();
        return File.OpenRead(path);
    }

    private static async Task<DiscoveryPackage> ReadFixtureAsync(string name)
    {
        await using var stream = OpenFixture(name);
        var package = (await new ZipPackageReader(new PackageValidator(), new PackageLimits())
            .ReadAsync(stream, cancellationToken: TestContext.Current.CancellationToken)).Package;
        DiscoveryCatalog.Load().ValidatePackage(package);
        return package;
    }
}
