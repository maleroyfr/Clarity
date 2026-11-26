using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class PowerBIScriptTests
{
    private const string Producer = "clarity.powerbi";
    private const string Tenant = "00000000-0000-0000-0000-000000000001";
    private static readonly CollectionScope Scope = new("tenant", Tenant);
    private static readonly string[] Sections = ["powerbi.workspaces", "powerbi.reports", "powerbi.semanticModels", "powerbi.owners",
        "powerbi.workspaceRoles", "powerbi.gateways", "powerbi.dataSources", "powerbi.modelSources"];

    [Fact]
    public async Task DownloadableBundleProducesContractEvidenceButCannotClaimExhaustiveCoverage()
    {
        var request = new ScriptParameters("1.0", Guid.NewGuid(), "M365Tenant", Tenant, Scope, Sections, "1.0.0", null);
        var bundle = ScriptBundle.Build(Producer, "1.0.0")!;
        bundle.Content.ShouldBe(ScriptBundle.Build(Producer, "1.0.0")!.Content);
        using var archive = new ZipArchive(new MemoryStream(bundle.Content));
        archive.Entries.Select(e => e.FullName).ShouldBe(["powerbi/Collect-PowerBI.ps1", "powerbi/Clarity.Discovery.PowerBI.psm1",
            "Clarity.Discovery.Common/Clarity.Discovery.Common.psd1", "Clarity.Discovery.Common/Clarity.Discovery.Common.psm1"]);
        var bytes = await CollectAsync(bundle, JsonSerializer.SerializeToUtf8Bytes(request, ContractJson.Options), "visible");
        var package = (await new ZipPackageReader(new PackageValidator(), new PackageLimits())
            .ReadAsync(new MemoryStream(bytes), cancellationToken: TestContext.Current.CancellationToken)).Package;
        var catalog = DiscoveryCatalog.Load();
        catalog.ValidatePackage(package);
        package.Manifest.RequestId.ShouldBe(request.RequestId);
        package.Results.Select(r => r.CapabilityId).ShouldBe(Sections);
        var first = package.Results[0];
        var overclaim = package with { Results = [first with { Status = CoverageStatus.Complete, Warnings = [], Errors = [] }, .. package.Results.Skip(1)] };
        Should.Throw<ContractException>(() => catalog.ValidatePackage(overclaim)).Code.ShouldBe("CatalogCoverage");
    }

    [SqlFact]
    public async Task RequestedBundlePublishesOnlyAfterConfirmationAndRetainsOwnerProofAcrossLimitedRefresh()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var project = fixture.Project.Id;
        var tenant = new ProjectEnvironment { WorkspaceId = fixture.Workspace.Id, ProjectId = project, ExternalId = Tenant, Kind = EnvironmentKind.M365Tenant };
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            db.Environments.Add(tenant);
            await db.SaveChangesAsync();
        }
        var catalog = DiscoveryCatalog.Load();
        var requests = new ScriptRequestService(fixture.Factory, fixture.Access, fixture.Scope, catalog);
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(fixture, store);
        var processor = ZipImportTests.Processor(fixture, store);
        var inventory = new InventoryQueries(fixture.Factory, fixture.Access);
        var sheet = new InventorySheet(fixture.Factory, fixture.Access);
        var filter = new InventoryFilter(tenant.Role, Environment: tenant.Id, Type: "powerBISemanticModel");

        // The owner was already collected in this environment; its UPN is dated evidence, not a new Power BI identity.
        var date = DateTimeOffset.UtcNow.AddDays(-1);
        var graph = new DiscoveryPackage(new("1.0", Guid.NewGuid(), "clarity.graph.entra", "1.0.0", "M365Tenant", Tenant, Scope,
            date, date, new Dictionary<string, string>(), "Synthetic Graph"),
            [new("entra.users", "user", "microsoft.graph", "70000000-0000-0000-0000-000000000001", date,
                new Dictionary<string, JsonElement> { ["userPrincipalName"] = JsonSerializer.SerializeToElement("owner@synthetic.example") })], [],
            [new("entra.users", "entra.users", CoverageStatus.Complete, Scope, ["userPrincipalName"], [], 1, 0, [], [])]);
        var graphRun = await imports.UploadAsync(project, tenant.Id, new MemoryStream(await ZipImportTests.BytesAsync(graph)), "synthetic-graph.zip");
        await imports.ConfirmAsync(project, graphRun);
        (await processor.ProcessOneAsync(default)).ShouldBeTrue();

        var (id, bytes) = await RequestAndCollectAsync("visible");
        var unrelated = Guid.NewGuid();
        await Should.ThrowAsync<AccessDeniedException>(() => requests.DownloadBundleAsync(unrelated, id));
        await Should.ThrowAsync<AccessDeniedException>(() => imports.UploadAsync(unrelated, tenant.Id, new MemoryStream(bytes), "result.zip"));
        var run = await imports.UploadAsync(project, tenant.Id, new MemoryStream(bytes), "result.zip");
        var preview = await imports.PreviewAsync(project, run);
        preview.NewObjects.ShouldBe(8);
        preview.Sections.ShouldAllBe(s => s.Status == CoverageStatus.Partial);
        (await inventory.PageAsync(project, filter, new())).TotalCount.ShouldBe(0);
        await imports.ConfirmAsync(project, run);
        (await processor.ProcessOneAsync(default)).ShouldBeTrue();
        var models = await inventory.PageAsync(project, filter, new());
        var model = models.Items.Single(x => x.DisplayName == "Synthetic model");
        var edge = (await sheet.EdgesAsync(project, model.Id, new())).Items.Single(x => x.Type == "modelOwnedBy");
        (edge.Resolution, edge.OtherLabel, edge.Current).ShouldBe((EndpointResolution.Alternate, "owner@synthetic.example", true));
        var proof = (await new InventoryProof(fixture.Factory, fixture.Access).ReadAsync(project, new(ProofKind.Relationship, edge.Id))).Sources.ShouldHaveSingleItem();
        (proof.Producer, proof.Version, proof.Channel, proof.FileName, proof.Scope, proof.Result)
            .ShouldBe((Producer, "1.0.0", "ZipImport", "result.zip", Scope, CoverageStatus.Partial));
        proof.Modules["MicrosoftPowerBIMgmt.Profile"].ShouldBe("1.3.84");
        proof.Uploader.ShouldBe(fixture.Identity);

        (await imports.UploadAsync(project, tenant.Id, new MemoryStream(bytes), "repeat.zip")).ShouldBe(run);
        await imports.ConfirmAsync(project, run);
        (await processor.ProcessOneAsync(default)).ShouldBeFalse();
        var (_, empty) = await RequestAndCollectAsync("empty");
        var later = await imports.UploadAsync(project, tenant.Id, new MemoryStream(empty), "limited.zip");
        await imports.ConfirmAsync(project, later);
        (await processor.ProcessOneAsync(default)).ShouldBeTrue();
        (await sheet.ReadAsync(project, model.Id)).Presence.ShouldBe(InventoryPresence.Observed);
        (await sheet.EdgesAsync(project, model.Id, new())).Items.Single(x => x.Type == "modelOwnedBy").Current.ShouldBeTrue();

        async Task<(Guid Id, byte[] Bytes)> RequestAndCollectAsync(string scenario)
        {
            var request = await requests.CreateAsync(project, tenant.Id, Guid.NewGuid(), Scope, Sections, null);
            var bundle = await requests.DownloadBundleAsync(project, request);
            bundle.FileName.ShouldBe("Clarity.Discovery.PowerBI.1.0.0.zip");
            return (request, await CollectAsync(bundle, await requests.DownloadParametersAsync(project, request), scenario));
        }
    }

    private static async Task<byte[]> CollectAsync(ScriptBundleFile bundle, byte[] parameters, string scenario)
    {
        var token = TestContext.Current.CancellationToken;
        var dir = Directory.CreateTempSubdirectory("clarity-powerbi-");
        try
        {
            ZipFile.ExtractToDirectory(new MemoryStream(bundle.Content), dir.FullName);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "parameters.json"), parameters, token);
            var info = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-File", Path.Combine(AppContext.BaseDirectory, "PowerBIFixture", "Run-DownloadedBundle.ps1"),
                    "-BundleDir", dir.FullName, "-Case", scenario }
            };
            using var process = Process.Start(info)!;
            await process.WaitForExitAsync(token);
            process.ExitCode.ShouldBe(0);
            return await File.ReadAllBytesAsync(Path.Combine(dir.FullName, "result.zip"), token);
        }
        finally { dir.Delete(recursive: true); }
    }
}
