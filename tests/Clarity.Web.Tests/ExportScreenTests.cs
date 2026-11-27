using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Clarity.Web.Tests;

public sealed class ExportScreenTests
{
    [Fact]
    public async Task ExportScreenOffersGenerationToConsultantsAndAuditedDownloadsToReadersOnly()
    {
        await using var app = new FoundationApplication(discoveryStorage: true);
        var (alpha, beta) = await app.SeedAsync();
        var token = TestContext.Current.CancellationToken;
        var workspace = DevelopmentAuthenticationHandler.AlphaWorkspace;
        var store = (TestExportStore)app.Services.GetRequiredService<IExportStore>();
        var export = new ExportJob
        {
            WorkspaceId = workspace,
            ProjectId = alpha,
            Side = EnvironmentRole.Source,
            FiltersJson = JsonSerializer.Serialize(new ExportFilter(EnvironmentRole.Source, Wave: "Vague 1"), ContractJson.Options),
            SelectionJson = "[]",
            SelectionHash = new string('b', 64),
            AsOfUtc = DateTimeOffset.UtcNow,
            AuthorTenantId = DevelopmentAuthenticationHandler.SyntheticTenant,
            AuthorObjectId = DevelopmentAuthenticationHandler.Personas["consultant"],
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ObjectCount = 1,
            ExceptionCount = 0,
            MappingCount = 0,
            CoverageGaps = 0
        };
        var content = Encoding.UTF8.GetBytes("Nom;Décision\r\nAlice;Inclure\r\n");
        var stored = await store.SaveAsync(workspace, export.Id, "csv", "text/csv", new MemoryStream(content), token);
        var artifact = new ExportArtifact
        {
            WorkspaceId = workspace,
            ExportJobId = export.Id,
            Kind = ExportArtifactKind.InventoryCsv,
            FileName = "clarity-source-inventory.csv",
            ContentType = "text/csv; charset=utf-8",
            BlobName = stored.BlobName,
            ContentHash = stored.ContentHash,
            Bytes = stored.Bytes,
            RowCount = 1
        };
        await using (var db = app.CreateUnscopedContext())
        {
            db.AddRange(export, artifact, new CollectionJob
            {
                Id = export.Id,
                WorkspaceId = workspace,
                ProjectId = alpha,
                ActorTenantId = export.AuthorTenantId,
                ActorObjectId = export.AuthorObjectId,
                Kind = CollectionJobKind.Export,
                PayloadJson = "{}",
                ConcurrencyKey = $"export:{export.Id:N}",
                CorrelationId = "synthetic",
                State = CollectionRunState.Completed,
                Progress = 100
            });
            await db.SaveChangesAsync(token);
        }

        using var consultant = await app.CreatePersonaClientAsync("consultant");
        var html = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/exports", token));
        html.ShouldContain("Nouvel export");
        html.ShouldContain("Générer l’export");
        html.ShouldContain("Exports du projet");

        using var reader = await app.CreatePersonaClientAsync("reader");
        var readOnly = WebUtility.HtmlDecode(await reader.GetStringAsync($"/projects/{alpha}/exports", token));
        readOnly.ShouldContain("Votre rôle permet de consulter et télécharger les exports de ce projet, pas d’en générer.");
        readOnly.ShouldNotContain("Générer l’export");
        var page = await reader.GetFromJsonAsync<JsonElement>($"/api/projects/{alpha}/exports", token);
        page.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var row = page.GetProperty("items")[0];
        row.GetProperty("objectCount").GetInt32().ShouldBe(1);
        row.GetProperty("artifacts")[0].GetProperty("contentHash").GetString().ShouldBe(stored.ContentHash);

        var url = $"/api/projects/{alpha}/exports/{export.Id}/artifacts/{artifact.Id}";
        using var download = await reader.GetAsync(url, token);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Headers.CacheControl!.NoStore.ShouldBeTrue();
        download.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("clarity-source-inventory.csv");
        (await download.Content.ReadAsByteArrayAsync(token)).ShouldBe(content);
        (await reader.PostAsJsonAsync($"/api/projects/{alpha}/exports",
            new ExportRequest(Guid.NewGuid(), new ExportFilter(EnvironmentRole.Source)), token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await consultant.GetAsync($"/api/projects/{beta}/exports/{export.Id}/artifacts/{artifact.Id}", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await consultant.GetAsync($"/api/projects/{alpha}/exports/{Guid.NewGuid()}/artifacts/{artifact.Id}", token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var admin = await app.CreatePersonaClientAsync("platform-admin");
        (await admin.GetAsync(url, token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using (var db = app.CreateUnscopedContext())
        {
            (await db.AuditEvents.CountAsync(e => e.Action == "ExportDownloaded" && e.ResourceId == artifact.Id, token)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task ExportScreenRequiresConfiguredStorageWithoutLocalFallback()
    {
        await using var app = new FoundationApplication();
        var (alpha, _) = await app.SeedAsync();
        using var consultant = await app.CreatePersonaClientAsync("consultant");
        var html = WebUtility.HtmlDecode(await consultant.GetStringAsync($"/projects/{alpha}/exports", TestContext.Current.CancellationToken));
        html.ShouldContain("Aucun stockage local de remplacement n'est utilisé.");
        html.ShouldNotContain("Générer l’export");
    }
}

internal sealed class TestExportStore : IExportStore
{
    private readonly ConcurrentDictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
    public bool IsConfigured => true;

    public async Task<StoredArtifact> SaveAsync(Guid workspaceId, Guid exportId, string extension, string contentType, Stream source,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        await source.CopyToAsync(output, cancellationToken);
        var bytes = output.ToArray();
        var name = $"{workspaceId:N}/{exportId:N}/{Guid.NewGuid():N}.{extension}";
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
