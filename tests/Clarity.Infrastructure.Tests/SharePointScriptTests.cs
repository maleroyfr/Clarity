using System.IO.Compression;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class SharePointScriptTests
{
    [Fact]
    public void SharePointAndOneDriveBundlesAreSeparateRegisteredFamilies()
    {
        using (var zip = new ZipArchive(new MemoryStream(ScriptBundle.Registry.Create("clarity.sharepoint")!)))
        {
            zip.Entries.Select(x => x.FullName).ShouldContain("sharepoint/Collect-SharePoint.ps1");
            zip.Entries.Select(x => x.FullName).ShouldNotContain("onedrive/Collect-OneDrive.ps1");
        }
        ScriptBundle.Build("clarity.onedrive", "1.0.0")!.FileName.ShouldBe("Clarity.Discovery.OneDrive.1.0.0.zip");
        // The SharePoint client ID and the OneDrive admin URL are prompted at launch; neither is a stored parameter.
        ScriptBundle.Command("clarity.sharepoint").ShouldBe(@".\sharepoint\Collect-SharePoint.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip");
        ScriptBundle.Command("clarity.onedrive").ShouldBe(@".\onedrive\Collect-OneDrive.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip");
        ScriptBundle.Registry.Producers(DiscoveryCatalog.Load(), "M365Tenant").Select(x => x.Id)
            .ShouldBe(["clarity.exchange", "clarity.onedrive", "clarity.sharepoint"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(" https://Synthetic.SharePoint.com/sites/Alpha/ ", "https://synthetic.sharepoint.com/sites/Alpha")]
    [InlineData("https://synthetic-admin.sharepoint.com", null)]
    [InlineData("https://synthetic-my.sharepoint.com/personal/user_synthetic_example", null)]
    [InlineData("http://synthetic.sharepoint.com/sites/Alpha", null)]
    [InlineData("https://synthetic.example/sites/Alpha", null)]
    public void ResourceScopeIsOneNormalizedSharePointSiteUrl(string value, string? expected)
    {
        ScriptRequestService.ResourceScope("clarity.sharepoint", value)?.Value.ShouldBe(expected);
        if (expected is null) { ScriptRequestService.ResourceScope("clarity.sharepoint", value).ShouldBeNull(); }
        ScriptRequestService.ResourceScope("clarity.onedrive", value).ShouldBeNull();
    }

    [SqlFact]
    public async Task SharePointRequestsTargetOneNormalizedSiteAndOneDriveRequestsTheTenant()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var tenant = Guid.NewGuid().ToString();
        ProjectEnvironment environment;
        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            environment = new ProjectEnvironment { WorkspaceId = fixture.Workspace.Id, ProjectId = fixture.Project.Id, Kind = EnvironmentKind.M365Tenant, ExternalId = tenant };
            db.Environments.Add(environment);
            await db.SaveChangesAsync();
        }
        var service = new ScriptRequestService(fixture.Factory, fixture.Access, fixture.Scope, DiscoveryCatalog.Load());
        var operation = Guid.NewGuid();
        string[] sections = ["sharepoint.siteDetails", "sharepoint.sitePermissions"];
        (await service.CreateAsync(fixture.Project.Id, environment.Id, operation, new("resource", "https://Synthetic.SharePoint.com/sites/Alpha/"),
            sections, null)).ShouldBe(operation);
        // The normalized scope is the idempotency identity, so the same site typed differently is the same request.
        (await service.CreateAsync(fixture.Project.Id, environment.Id, operation, new("resource", "https://synthetic.sharepoint.com/sites/Alpha"),
            sections, null)).ShouldBe(operation);
        var parameters = JsonSerializer.Deserialize<ScriptParameters>(await service.DownloadParametersAsync(fixture.Project.Id, operation), ContractJson.Options)!;
        (parameters.ExpectedEnvironmentExternalId, parameters.Scope, parameters.Domain)
            .ShouldBe((tenant, new CollectionScope("resource", "https://synthetic.sharepoint.com/sites/Alpha"), null));
        (await service.DownloadBundleAsync(fixture.Project.Id, operation)).FileName.ShouldBe("Clarity.Discovery.SharePoint.1.0.0.zip");

        foreach (var invalid in new[] { "https://synthetic-admin.sharepoint.com", "https://synthetic-my.sharepoint.com/personal/user_synthetic_example", "not a url" })
        {
            await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(),
                new("resource", invalid), sections, null));
        }
        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(),
            new("resource", "https://synthetic.sharepoint.com/sites/Alpha"), sections, "example.test"));
        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(),
            new("tenant", tenant), sections, null));
        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(),
            new("resource", "https://synthetic.sharepoint.com/sites/Alpha"), ["onedrive.personalSites"], null));

        var personal = await service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(), new("tenant", tenant), ["onedrive.personalSites"], null);
        var personalParameters = JsonSerializer.Deserialize<ScriptParameters>(await service.DownloadParametersAsync(fixture.Project.Id, personal), ContractJson.Options)!;
        personalParameters.Scope.ShouldBe(new CollectionScope("tenant", tenant));
        (await service.DownloadBundleAsync(fixture.Project.Id, personal)).FileName.ShouldBe("Clarity.Discovery.OneDrive.1.0.0.zip");
        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(),
            new("tenant", tenant), ["onedrive.personalSites", "sharepoint.siteDetails"], null));
    }
}
