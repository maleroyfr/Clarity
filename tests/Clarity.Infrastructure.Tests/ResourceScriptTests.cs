using System.IO.Compression;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class ResourceScriptTests
{
    private const string Tenant = "55555555-0000-0000-0000-000000000001";
    private const string Subscription = "/subscriptions/55555555-0000-0000-0000-000000000002";

    [Theory]
    [InlineData(Subscription, true)]
    [InlineData(Subscription + "/resourceGroups/Pilot-1", true)]
    [InlineData(Subscription + "/resourceGroups/*", false)]
    [InlineData("/subscriptions/55555555-0000-0000-0000-000000000099", false)]
    public void AzureResourceRequestsCannotEscapeTheDeclaredSubscription(string path, bool allowed) =>
        ResourceScriptScope.Allows(EnvironmentKind.AzureScope, Tenant + Subscription, new("resource", path)).ShouldBe(allowed);

    [Theory]
    [InlineData("server.example.test", true)]
    [InlineData("server.other.test", false)]
    [InlineData("server", false)]
    [InlineData("server.sub.example.test", false)]
    [InlineData("*.example.test", false)]
    public void InfrastructureTargetsOneHostInTheDeclaredDomain(string fqdn, bool allowed) =>
        ResourceScriptScope.Allows(EnvironmentKind.Other, "example.test", new("resource", fqdn)).ShouldBe(allowed);

    [Theory]
    [InlineData("azure-group", EnvironmentKind.AzureScope)]
    [InlineData("infra-complete", EnvironmentKind.Other)]
    public async Task ResourceEvidenceCannotCrossTheRequestedBoundaryEvenWithMatchingManifest(string file, EnvironmentKind kind)
    {
        var (_, package) = await ReadFixture(file);
        ResourceScriptScope.MatchesEvidence(kind, package).ShouldBeTrue();
        var item = package.Objects[0];
        ResourceScriptScope.MatchesEvidence(kind, package with { Objects = [item with { ExternalId = "foreign.test/privacy" }] }).ShouldBeFalse();
        var edge = package.Relationships[0];
        ResourceScriptScope.MatchesEvidence(kind, package with
        {
            Relationships = [edge with { To = edge.To with { ExternalId = package.Manifest.Scope.Value + "-other/child" } }]
        }).ShouldBeFalse();
        ResourceScriptScope.MatchesEvidence(kind, package with
        {
            Relationships = [edge with { Properties = new Dictionary<string, JsonElement> { ["note"] = JsonSerializer.SerializeToElement("unexpected") } }]
        }).ShouldBeFalse();
        ResourceScriptScope.MatchesEvidence(kind, package with
        {
            Results = package.Results.Select(s => s with { Warnings = [] }).ToArray()
        }).ShouldBeFalse();
    }

    [SqlFact]
    public Task VerifiedAzureRequestImportsGeneratedPackagesAndPreservesPreviousWorkspaceEvidence() =>
        RunJourney("azure", EnvironmentKind.AzureScope, 3, 2, "workspaceInResourceGroup", "azure.logAnalyticsWorkspaces", "Az.Accounts");

    [SqlFact]
    public Task VerifiedHostRequestImportsGeneratedPackagesAndLeavesUnseenDependencyUnresolved() =>
        RunJourney("infra", EnvironmentKind.Other, 8, 9, "applicationUsesPool", "infrastructure.dnsZones", "CimCmdlets");

    private static async Task RunJourney(string prefix, EnvironmentKind kind, int objects, int edges, string relation, string failedSection, string module)
    {
        await using var f = await DiscoveryFixture.CreateAsync();
        var catalog = DiscoveryCatalog.Load();
        var requests = new ScriptRequestService(f.Factory, f.Access, f.Scope, catalog);
        var (bytes, full) = await ReadFixture(prefix + "-complete");
        var env = new ProjectEnvironment { WorkspaceId = f.Workspace.Id, ProjectId = f.Project.Id, Kind = kind, ExternalId = full.Manifest.EnvironmentExternalId };
        await using var db = await f.Factory.CreateDbContextAsync();
        db.Add(env);
        await db.SaveChangesAsync();
        var project = f.Project.Id;
        var store = new MemoryArtifactStore();
        var imports = ZipImportTests.Service(f, store);
        var processor = ZipImportTests.Processor(f, store);
        var manifest = full.Manifest;
        var sections = full.Results.Select(x => x.CapabilityId).ToArray();
        var requestId = await requests.CreateAsync(project, env.Id, manifest.RequestId!.Value, manifest.Scope, sections, null);
        (await requests.CreateAsync(project, env.Id, requestId, manifest.Scope, sections, null)).ShouldBe(requestId);
        var parameters = JsonSerializer.Deserialize<ScriptParameters>(await requests.DownloadParametersAsync(project, requestId), ContractJson.Options)!;
        parameters.Sections.ShouldBe(sections);
        (parameters.Scope, parameters.ExpectedEnvironmentExternalId, parameters.Domain).ShouldBe((manifest.Scope, env.ExternalId, null));
        var bundle = await requests.DownloadBundleAsync(project, requestId);
        bundle.Content.ShouldBe(ScriptBundle.Registry.Create(manifest.ProducerId)!);
        using var zip = new ZipArchive(new MemoryStream(bundle.Content));
        zip.Entries.Count.ShouldBe(4);
        zip.Entries.ShouldNotContain(x => x.FullName.Contains("parameters", StringComparison.OrdinalIgnoreCase));
        var other = await f.OtherWorkspaceAsync();
        await Should.ThrowAsync<AccessDeniedException>(() => new ScriptRequestService(other.Factory, other.Access, other.Scope, catalog)
            .DownloadBundleAsync(project, requestId));
        foreach (var invalid in new[] { full with { Manifest = manifest with { RequestId = null } },
            full with { Manifest = manifest with { EnvironmentExternalId = "foreign" } },
            full with { Objects = [.. full.Objects.Skip(1), full.Objects[0] with { ExternalId = "foreign" }] } })
        {
            await Should.ThrowAsync<ContractException>(async () => await imports.UploadAsync(project, env.Id,
                new MemoryStream(await ZipImportTests.BytesAsync(invalid)), "rejected.zip"));
        }
        var id = await imports.UploadAsync(project, env.Id, new MemoryStream(bytes), "synthetic.zip");
        (await imports.UploadAsync(project, env.Id, new MemoryStream(bytes), "duplicate.zip")).ShouldBe(id);
        (await imports.PreviewAsync(project, id)).NewObjects.ShouldBe(objects);
        (await db.InventoryObjects.CountAsync()).ShouldBe(0);
        await imports.ConfirmAsync(project, id);
        await imports.ConfirmAsync(project, id);
        await processor.ProcessOneAsync(default);
        (await db.InventoryObjects.CountAsync()).ShouldBe(objects);
        (await db.InventoryRelationships.CountAsync()).ShouldBe(edges);
        var proofTarget = new ProofRef(ProofKind.Relationship, await db.InventoryRelationships.Where(x => x.RelationType == relation).Select(x => x.Id).SingleAsync());
        var proof = await new InventoryProof(f.Factory, f.Access).ReadAsync(project, proofTarget);
        proof.To.ShouldBe(EndpointResolution.Primary);
        var source = proof.Sources.ShouldHaveSingleItem();
        (source.Channel, source.Producer, source.FileName, source.Scope).ShouldBe(("ZipImport", manifest.ProducerId, "synthetic.zip", manifest.Scope));
        source.Modules.Keys.ShouldContain(module);
        source.Uploader.ShouldBe(f.Identity);
        await Should.ThrowAsync<AccessDeniedException>(() => new InventoryProof(other.Factory, other.Access).ReadAsync(other.Project.Id, proofTarget));
        if (kind == EnvironmentKind.Other)
        {
            (await db.InventoryRelationships.SingleAsync(x => x.RelationType == "serviceDependsOnService")).ToMethod.ShouldBe(EndpointResolution.Pending);
        }

        var (partialBytes, partial) = await ReadFixture(prefix + "-partial");
        await requests.CreateAsync(project, env.Id, partial.Manifest.RequestId!.Value, partial.Manifest.Scope, sections, null);
        var retry = await imports.UploadAsync(project, env.Id, new MemoryStream(partialBytes), "partial.zip");
        await imports.ConfirmAsync(project, retry);
        await processor.ProcessOneAsync(default);
        var failed = await db.SectionRuns.SingleAsync(x => x.RunId == retry && x.CapabilityId == failedSection);
        (failed.Coverage, failed.ObjectCount).ShouldBe((CoverageStatus.Failed, null));
        (await db.InventoryObjects.CountAsync()).ShouldBe(objects);
        (await db.InventoryRelationships.CountAsync(x => x.IsCurrent)).ShouldBe(edges);
        (await db.CollectionRequests.CountAsync(x => x.State == CollectionRequestState.Integrated)).ShouldBe(2);
    }

    private static async Task<(byte[] Bytes, DiscoveryPackage Package)> ReadFixture(string file)
    {
        var directory = Environment.GetEnvironmentVariable("CLARITY_SCRIPT_FIXTURES");
        if (string.IsNullOrEmpty(directory)) { Assert.Skip("Set CLARITY_SCRIPT_FIXTURES and run Pester to validate generated packages."); }
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, file + ".zip"), TestContext.Current.CancellationToken);
        var package = (await new ZipPackageReader(new PackageValidator(), new PackageLimits())
            .ReadAsync(new MemoryStream(bytes), cancellationToken: TestContext.Current.CancellationToken)).Package;
        DiscoveryCatalog.Load().ValidatePackage(package);
        return (bytes, package);
    }
}
