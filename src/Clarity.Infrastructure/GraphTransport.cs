using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Clarity.Contracts;
using Clarity.Core;

namespace Clarity.Infrastructure;

public sealed record GraphCollectorOptions(string? ClientId, string? KeyVaultUri, string? CertificateName, string? ManagedIdentityClientId);
public sealed class GraphReadException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public interface IGraphSessionFactory
{
    Task<GraphSession> CreateAsync(Connection connection, CancellationToken cancellationToken);
}

public sealed class GraphSession(GraphTransport transport, IDisposable? credentialMaterial = null) : IDisposable
{
    public GraphTransport Transport => transport;
    public void Dispose() => credentialMaterial?.Dispose();
}

public sealed class KeyVaultGraphSessionFactory(Func<HttpClient> createClient, GraphCollectorOptions options) : IGraphSessionFactory
{
    public async Task<GraphSession> CreateAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (connection.ConfigurationReference != "collector" || !Guid.TryParse(options.ClientId, out var clientId)
            || !Uri.TryCreate(options.KeyVaultUri, UriKind.Absolute, out var vault)
            || vault.Scheme != "https" || !string.IsNullOrEmpty(vault.UserInfo) || !string.IsNullOrEmpty(vault.Query)
            || string.IsNullOrWhiteSpace(options.CertificateName))
        {
            throw new GraphReadException("CollectorNotConfigured");
        }
        var identity = string.IsNullOrEmpty(options.ManagedIdentityClientId)
            ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId));
        var certificates = new CertificateClient(vault, identity);
        var certificate = (await certificates.DownloadCertificateAsync(
            new DownloadCertificateOptions(options.CertificateName) { KeyStorageFlags = X509KeyStorageFlags.EphemeralKeySet },
            cancellationToken)).Value;
        try
        {
            var credential = new ClientCertificateCredential(connection.TenantId.ToString(), clientId.ToString(), certificate);
            return new GraphSession(new GraphTransport(createClient(), async token =>
                (await credential.GetTokenAsync(new TokenRequestContext(["https://graph.microsoft.com/.default"]), token)).Token), certificate);
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }
}

public sealed class GraphTransport(HttpClient http, Func<CancellationToken, ValueTask<string>> token,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private long receivedBytes;
    public static Uri ValidateUri(string path)
    {
        var uri = path.StartsWith("/", StringComparison.Ordinal)
            ? new Uri(new Uri("https://graph.microsoft.com"), path) : new Uri(path, UriKind.Absolute);
        if (uri.Scheme != "https" || uri.Host != "graph.microsoft.com" || uri.Port != 443
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.Ordinal)
            || uri.AbsoluteUri.Length > 16384)
        {
            throw new GraphReadException("UnsafeGraphLink");
        }
        return uri;
    }

    public async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        var uri = ValidateUri(path);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(cancellationToken));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = new LimitedReadStream(await response.Content.ReadAsStreamAsync(cancellationToken), 2 * 1024 * 1024);
                var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
                receivedBytes += stream.Position;
                if (receivedBytes > 128 * 1024 * 1024)
                {
                    document.Dispose();
                    throw new GraphReadException("ResponseSizeLimit");
                }
                return document;
            }
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
            {
                if (attempt == 3) { throw new GraphReadException("Throttled"); }
                var wait = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
                    ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                if (wait > TimeSpan.FromSeconds(60)) { throw new GraphReadException("RetryAfterLimit"); }
                wait = TimeSpan.FromSeconds(Math.Max(wait.TotalSeconds, 0));
                await (delay?.Invoke(wait, cancellationToken) ?? Task.Delay(wait, cancellationToken));
                continue;
            }
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                await using var errorStream = new LimitedReadStream(await response.Content.ReadAsStreamAsync(cancellationToken), 2 * 1024 * 1024);
                try
                {
                    using var error = await JsonDocument.ParseAsync(errorStream, cancellationToken: cancellationToken);
                    if (error.RootElement.TryGetProperty("error", out var detail) && detail.TryGetProperty("code", out var code)
                        && code.GetString() == "syncStateNotFound") { throw new GraphReadException("CursorExpired"); }
                }
                catch (JsonException) { throw new GraphReadException("InvalidGraphError"); }
            }
            throw new GraphReadException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "AuthenticationRequired",
                HttpStatusCode.Forbidden => "MissingPermission",
                HttpStatusCode.Gone => "CursorExpired",
                _ => "GraphReadFailed"
            });
        }
        throw new GraphReadException("GraphReadFailed");
    }

    public async Task<string?> PagesAsync(string path, Func<JsonElement, Task> consume, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? next = path;
        while (next is not null)
        {
            if (!visited.Add(next) || visited.Count > 1000) { throw new GraphReadException("PagingLimit"); }
            using var page = await GetAsync(next, cancellationToken);
            if (!page.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                throw new GraphReadException("InvalidGraphPage");
            }
            foreach (var item in values.EnumerateArray()) { await consume(item); }
            next = page.RootElement.TryGetProperty("@odata.nextLink", out var nextLink) ? nextLink.GetString() : null;
            if (page.RootElement.TryGetProperty("@odata.deltaLink", out var deltaLink))
            {
                if (next is not null || deltaLink.ValueKind != JsonValueKind.String) { throw new GraphReadException("InvalidGraphPage"); }
                return ValidateUri(deltaLink.GetString()!).AbsoluteUri;
            }
        }
        return null;
    }

    public async Task VerifyTenantAsync(Guid expected, CancellationToken cancellationToken)
    {
        using var organization = await GetAsync("/v1.0/organization?$select=id", cancellationToken);
        var values = organization.RootElement.GetProperty("value");
        if (values.GetArrayLength() != 1 || !Guid.TryParse(values[0].GetProperty("id").GetString(), out var observed) || observed != expected)
        {
            throw new ContractException("EnvironmentMismatch", "Graph");
        }
    }
}
