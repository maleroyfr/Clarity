using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Security;
using Clarity.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Clarity.Web.Tests;

public sealed class DiscoveryEndpointTests
{
    [Fact]
    public async Task EveryDiscoveryBoundaryRejectsOtherWorkspaceAndReaderMutations()
    {
        await using var app = new FoundationApplication();
        var (alpha, beta) = await app.SeedAsync();
        var environment = await SeedEnvironmentAsync(app, alpha);
        using var manager = await app.CreatePersonaClientAsync("manager");
        using var reader = await app.CreatePersonaClientAsync("reader");
        var input = new ScriptInput(Guid.NewGuid(), new("domain", "example.test"), ["ad.users"], null);
        foreach (var (client, project) in new[] { (manager, beta), (reader, alpha) })
        {
            var root = $"/api/projects/{project}/discovery";
            (await client.PostAsJsonAsync($"{root}/{environment}/requests", input, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync($"{root}/{environment}/connection", new ConnectionInput("Synthetic"), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PostAsJsonAsync($"{root}/{environment}/graph", new GraphInput(Guid.NewGuid(), Guid.NewGuid(), new("tenant", "synthetic"), ["entra.users"]),
                TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PostAsync($"{root}/{environment}/upload", new ByteArrayContent([1, 2]), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PostAsync($"{root}/runs/{Guid.NewGuid()}/confirm", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            foreach (var action in new[] { "cancel", "retry" })
            {
                (await client.PostAsync($"{root}/jobs/{Guid.NewGuid()}/{action}", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            }
        }
        foreach (var path in new[]
        {
            $"{environment}/runs", $"{environment}/requests", $"{environment}/coverage", $"{environment}/connection", "jobs",
            $"runs/{Guid.NewGuid()}/preview", $"runs/{Guid.NewGuid()}/download",
            $"requests/{Guid.NewGuid()}/parameters", $"requests/{Guid.NewGuid()}/bundle"
        })
        {
            (await manager.GetAsync($"/api/projects/{beta}/discovery/{path}", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        using var admin = await app.CreatePersonaClientAsync("platform-admin");
        (await admin.GetAsync($"/api/projects/{alpha}/discovery/{environment}/coverage", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FrenchDiscoveryScreensShowRealActionsAndUnknownCoverageWithoutInventingData()
    {
        await using var app = new FoundationApplication();
        var (alpha, _) = await app.SeedAsync();
        await SeedEnvironmentAsync(app, alpha);
        using var client = await app.CreatePersonaClientAsync("manager");
        var collections = WebUtility.HtmlDecode(await client.GetStringAsync($"/projects/{alpha}/discovery?view=Collections", TestContext.Current.CancellationToken));
        collections.ShouldContain("Connexions");
        collections.ShouldContain("Créer une demande de script AD");
        collections.ShouldContain("Demandes de scripts");
        collections.ShouldContain("Historique des résultats");
        collections.ShouldContain("Aucun stockage local de remplacement");
        collections.ShouldNotContain("Aucune collecte ni import n'est encore lancé");
        var coverage = WebUtility.HtmlDecode(await client.GetStringAsync($"/projects/{alpha}/discovery?view=Coverage", TestContext.Current.CancellationToken));
        coverage.ShouldContain("AD — unités d'organisation");
        coverage.ShouldContain("Inconnu — à collecter");
        coverage.ShouldContain("0 confirmé signifie une lecture complète");
        using var noToken = app.CreateClient();
        (await noToken.PostAsJsonAsync($"/api/projects/{alpha}/discovery/{Guid.NewGuid()}/requests",
            new ScriptInput(Guid.NewGuid(), new("domain", "example.test"), ["ad.users"], null), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SqlSeparateMachineJourneyDownloadsUploadsPreviewsConfirmsAndPreservesCoverage()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLARITY_TEST_SQL")))
        {
            Assert.Skip("SQL integration test skipped: set CLARITY_TEST_SQL to a disposable SQL Server (provided by CI).");
        }
        await using var app = new FoundationApplication(sql: true, discoveryStorage: true);
        var (alpha, _) = await app.SeedAsync();
        var environment = await SeedEnvironmentAsync(app, alpha);
        using var requester = await app.CreatePersonaClientAsync("manager");
        var root = $"/api/projects/{alpha}/discovery";
        var requestId = Guid.NewGuid();
        var request = await requester.PostAsJsonAsync($"{root}/{environment}/requests",
            new ScriptInput(requestId, new("domain", "example.test"), ["ad.users"], null), TestContext.Current.CancellationToken);
        request.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await requester.GetByteArrayAsync($"{root}/requests/{requestId}/bundle", TestContext.Current.CancellationToken)).ShouldBe(ScriptBundle.BuildAd());
        var parameters = await requester.GetFromJsonAsync<ScriptParameters>($"{root}/requests/{requestId}/parameters", TestContext.Current.CancellationToken);
        parameters!.RequestId.ShouldBe(requestId);
        var date = DateTimeOffset.UtcNow;
        var package = new DiscoveryPackage(new PackageManifest("1.0", Guid.NewGuid(), "clarity.ad", "1.0.0", "AdForest", "forest.example.test",
            parameters.Scope, date, date, new Dictionary<string, string>(), "Synthetic separate machine", requestId),
            [new("ad.users", "user", "ad.objectGUID", "synthetic-user", date,
                new Dictionary<string, JsonElement> { ["displayName"] = JsonSerializer.SerializeToElement("Synthetic") })], [],
            [new("ad.users", "ad.users", CoverageStatus.Complete, parameters.Scope, ["displayName"], [], 1, 0, [], [])]);
        using var zip = new MemoryStream();
        await PackageZipWriter.WriteAsync(package, zip, TestContext.Current.CancellationToken);
        using var otherMachine = await app.CreatePersonaClientAsync("consultant");
        var upload = await otherMachine.PostAsync($"{root}/{environment}/upload", new ByteArrayContent(zip.ToArray()), TestContext.Current.CancellationToken);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var runId = await upload.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
        var preview = await otherMachine.GetFromJsonAsync<PreviewView>($"{root}/runs/{runId}/preview", TestContext.Current.CancellationToken);
        preview!.NewObjects.ShouldBe(1);
        await using (var db = app.CreateUnscopedContext()) { (await db.ObjectObservations.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0); }
        (await otherMachine.PostAsync($"{root}/runs/{runId}/confirm", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        string connection;
        await using (var db = app.CreateUnscopedContext()) { connection = db.Database.GetConnectionString()!; }
        var store = app.Services.GetRequiredService<IArtifactStore>();
        var processor = new CollectionJobProcessor(new SqlJobQueue(connection, TimeProvider.System),
            [new ImportJobHandler(store, new(new(), new()), new(), DiscoveryCatalog.Load(), new())], NullLogger<CollectionJobProcessor>.Instance);
        await processor.ProcessOneAsync(TestContext.Current.CancellationToken);
        var coverage = await otherMachine.GetFromJsonAsync<PageResult<CoverageView>>($"{root}/{environment}/coverage", TestContext.Current.CancellationToken);
        coverage!.Items.ShouldHaveSingleItem().LastValidRunId.ShouldBe(runId);
        coverage.Items[0].ObjectCount.ShouldBe(1);
        var lastValid = coverage.Items[0].LastValidAtUtc;
        var failed = package with
        {
            Manifest = package.Manifest with { RunId = Guid.NewGuid(), RequestId = null, StartedAtUtc = date.AddMinutes(1), CompletedAtUtc = date.AddMinutes(1) },
            Objects = [],
            Results = [package.Results[0] with { Status = CoverageStatus.Failed, ObjectCount = null, RelationshipCount = null, Errors = [new("MissingRead", "Synthetic missing read permission.")] }]
        };
        using var failedZip = new MemoryStream();
        await PackageZipWriter.WriteAsync(failed, failedZip, TestContext.Current.CancellationToken);
        var failedUpload = await otherMachine.PostAsync($"{root}/{environment}/upload", new ByteArrayContent(failedZip.ToArray()), TestContext.Current.CancellationToken);
        var failedId = await failedUpload.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
        coverage = await otherMachine.GetFromJsonAsync<PageResult<CoverageView>>($"{root}/{environment}/coverage", TestContext.Current.CancellationToken);
        coverage!.Items[0].Coverage.ShouldBeNull();
        coverage.Items[0].LastValidAtUtc.ShouldBe(lastValid);
        (await otherMachine.PostAsync($"{root}/runs/{failedId}/confirm", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await processor.ProcessOneAsync(TestContext.Current.CancellationToken);
        coverage = await otherMachine.GetFromJsonAsync<PageResult<CoverageView>>($"{root}/{environment}/coverage", TestContext.Current.CancellationToken);
        coverage!.Items[0].Coverage.ShouldBe(CoverageStatus.Failed);
        coverage.Items[0].LastValidAtUtc.ShouldBe(lastValid);
        (await otherMachine.GetByteArrayAsync($"{root}/runs/{runId}/download", TestContext.Current.CancellationToken)).ShouldBe(zip.ToArray());
        await using var check = app.CreateUnscopedContext();
        (await check.ObjectObservations.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await check.CollectionRequests.SingleAsync(TestContext.Current.CancellationToken)).State.ShouldBe(CollectionRequestState.Integrated);
    }

    [Fact]
    public async Task PreviewNeverSerializesPrivateGraphContinuations()
    {
        await using var app = new FoundationApplication();
        var (alpha, _) = await app.SeedAsync();
        var id = await SeedEnvironmentAsync(app, alpha);
        var scope = new CollectionScope("domain", "example.test");
        await using var db = app.CreateUnscopedContext();
        db.CollectionRuns.Add(new CollectionRun
        {
            WorkspaceId = DevelopmentAuthenticationHandler.AlphaWorkspace,
            ProjectId = alpha,
            EnvironmentId = id,
            ExternalRunId = Guid.NewGuid(),
            ProducerId = "clarity.graph.entra",
            ProducerVersion = "1.0.0",
            ScopeJson = DiscoveryScope.Serialize(scope),
            ScopeHash = DiscoveryScope.Hash(scope),
            ContentHash = new string('0', 64),
            ManifestJson = "{}",
            ContinuationJson = "private-delta-marker",
            CorrelationId = "synthetic"
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var reader = await app.CreatePersonaClientAsync("reader");
        var json = await reader.GetStringAsync($"/api/projects/{alpha}/discovery/{id}/runs", TestContext.Current.CancellationToken);
        json.ShouldNotContain("private-delta-marker");
        json.ShouldNotContain("continuationJson");
    }

    private static async Task<Guid> SeedEnvironmentAsync(FoundationApplication app, Guid project)
    {
        await using var db = app.CreateUnscopedContext();
        var environment = new ProjectEnvironment
        {
            WorkspaceId = DevelopmentAuthenticationHandler.AlphaWorkspace,
            ProjectId = project,
            Kind = EnvironmentKind.AdForest,
            ExternalId = "forest.example.test"
        };
        db.Environments.Add(environment);
        await db.SaveChangesAsync();
        return environment.Id;
    }
}

internal sealed class TestArtifactStore : IArtifactStore
{
    private readonly ConcurrentDictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
    public async Task<StoredArtifact> SaveAsync(Guid workspaceId, Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        await source.CopyToAsync(output, cancellationToken);
        var bytes = output.ToArray();
        if (bytes.LongLength > maxBytes) { throw new ContractException("CompressedSizeLimit", "zip"); }
        var name = $"{workspaceId:N}/{Guid.NewGuid():N}.zip";
        blobs[name] = bytes;
        return new(name, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);
    }
    public Task<Stream> OpenAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken)
    {
        if (!blobName.StartsWith($"{workspaceId:N}/", StringComparison.Ordinal)) { throw new AccessDeniedException(); }
        return Task.FromResult<Stream>(new MemoryStream(blobs[blobName]));
    }
    public Task DeleteAsync(Guid workspaceId, string blobName, CancellationToken cancellationToken)
    {
        if (!blobName.StartsWith($"{workspaceId:N}/", StringComparison.Ordinal)) { throw new AccessDeniedException(); }
        blobs.TryRemove(blobName, out _);
        return Task.CompletedTask;
    }
}
