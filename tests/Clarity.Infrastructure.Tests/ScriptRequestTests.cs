using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Shouldly;

namespace Clarity.Infrastructure.Tests;

[Collection("SQL lifecycle")]
public sealed class ScriptRequestTests
{
    [Fact]
    public void PublishedBundleIsDeterministicAndIncludesCommonComponentsWithoutParameters()
    {
        var first = ScriptBundle.BuildAd();
        first.ShouldBe(ScriptBundle.BuildAd());
        using var zip = new ZipArchive(new MemoryStream(first));
        zip.Entries.Select(x => x.FullName).ShouldBe([
            "ad/Collect-AD.ps1", "ad/Clarity.Discovery.AD.psm1",
            "Clarity.Discovery.Common/Clarity.Discovery.Common.psd1",
            "Clarity.Discovery.Common/Clarity.Discovery.Common.psm1"]);
    }

    [SqlFact]
    public async Task ParametersArePortableMetadataAndDownloadsReauthorizeTheProject()
    {
        await using var fixture = await DiscoveryFixture.CreateAsync();
        var service = new ScriptRequestService(fixture.Factory, fixture.Access, fixture.Scope, DiscoveryCatalog.Load());
        var id = await service.CreateAsync(fixture.Project.Id, fixture.Environment.Id, Guid.NewGuid(),
            new CollectionScope("domain", "example.test"), ["ad.users"], null);
        var bytes = await service.DownloadParametersAsync(fixture.Project.Id, id);
        var parameters = JsonSerializer.Deserialize<ScriptParameters>(bytes, ContractJson.Options)!;
        parameters.RequestId.ShouldBe(id);
        parameters.Domain.ShouldBe("example.test");
        Encoding.UTF8.GetString(bytes).ShouldNotContain("credential", Case.Insensitive);
        Encoding.UTF8.GetString(bytes).ShouldNotContain("token", Case.Insensitive);
        await Should.ThrowAsync<AccessDeniedException>(() => service.DownloadParametersAsync(Guid.NewGuid(), id));
        (await service.DownloadBundleAsync(fixture.Project.Id, id)).ShouldBe(ScriptBundle.BuildAd());
    }
}
