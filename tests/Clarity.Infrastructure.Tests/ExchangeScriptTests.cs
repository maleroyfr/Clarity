using System.IO.Compression;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class ExchangeScriptTests
{
    [Fact]
    public void ExchangeBundleIsDeterministicAndSeparateFromTheAdBundle()
    {
        var first = ScriptBundle.Registry.Create("clarity.exchange")!;
        first.ShouldBe(ScriptBundle.Registry.Create("clarity.exchange")!);
        first.ShouldNotBe(ScriptBundle.BuildAd());
        using var zip = new ZipArchive(new MemoryStream(first));
        zip.Entries.Select(x => x.FullName).ShouldBe([
            "exchange/Collect-Exchange.ps1", "exchange/Clarity.Discovery.Exchange.psm1",
            "Clarity.Discovery.Common/Clarity.Discovery.Common.psd1",
            "Clarity.Discovery.Common/Clarity.Discovery.Common.psm1"]);
        var published = ScriptBundle.Build("clarity.exchange", "1.0.0")!;
        published.Content.ShouldBe(first);
        published.FileName.ShouldBe("Clarity.Discovery.Exchange.1.0.0.zip");
        ScriptBundle.Command("clarity.exchange").ShouldBe(@".\exchange\Collect-Exchange.ps1 -ParameterFile .\parameters.json -OutputPath .\result.zip");
        ScriptBundle.Build("clarity.graph.entra", "1.0.0").ShouldBeNull();
    }

    [SqlFact]
    public async Task ExchangeRequestsAreBoundToTheTenantAndTheirOwnProducer()
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
        string[] sections = ["exchange.mailboxes", "exchange.mailboxPermissions"];
        var id = await service.CreateAsync(fixture.Project.Id, environment.Id, operation, new("tenant", tenant.ToUpperInvariant()), sections, null);
        id.ShouldBe(operation);
        (await service.CreateAsync(fixture.Project.Id, environment.Id, operation, new("tenant", tenant), sections, null)).ShouldBe(operation);

        var parameters = JsonSerializer.Deserialize<ScriptParameters>(await service.DownloadParametersAsync(fixture.Project.Id, id), ContractJson.Options)!;
        (parameters.EnvironmentKind, parameters.ExpectedEnvironmentExternalId, parameters.Scope).ShouldBe(("M365Tenant", tenant, new CollectionScope("tenant", tenant)));
        (parameters.Domain, parameters.ProducerVersion).ShouldBe((null, "1.0.0"));
        var bundle = await service.DownloadBundleAsync(fixture.Project.Id, id);
        bundle.FileName.ShouldBe("Clarity.Discovery.Exchange.1.0.0.zip");
        bundle.Content.ShouldBe(ScriptBundle.Registry.Create("clarity.exchange")!);

        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(),
            new("tenant", Guid.NewGuid().ToString()), sections, null));
        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(),
            new("tenant", tenant), sections, "example.test"));
        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(),
            new("tenant", tenant), ["ad.users"], null));
        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, Guid.NewGuid(),
            new("tenant", tenant), ["entra.users"], null));
        await Should.ThrowAsync<ArgumentException>(() => service.CreateAsync(fixture.Project.Id, fixture.Environment.Id, Guid.NewGuid(),
            new("domain", "example.test"), sections, null));
        await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(fixture.Project.Id, environment.Id, operation,
            new("tenant", tenant), ["exchange.mailboxes"], null));
    }
}
