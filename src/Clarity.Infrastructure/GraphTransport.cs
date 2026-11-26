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

    public async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken) =>
        await SendAsync(path, false, cancellationToken) ?? throw new GraphReadException("GraphReadFailed");

    /// <summary>Reads a Graph resource whose absence is an expected outcome; returns null on HTTP 404.</summary>
    public Task<JsonDocument?> GetOptionalAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(path, true, cancellationToken);

    /// <summary>
    /// Requests a Graph v1.0 usage report. Graph answers with a redirect to a pre-authenticated download URL that is fetched
    /// without the Graph bearer token, only on an allow-listed Microsoft reports host and within <paramref name="maxBytes"/>.
    /// A probe stops after Graph accepted the request and returns null.
    /// </summary>
    public async Task<byte[]?> DownloadReportAsync(string path, long maxBytes, bool probe, CancellationToken cancellationToken)
    {
        var uri = ValidateUri(path);
        if (!uri.AbsolutePath.StartsWith("/v1.0/reports/", StringComparison.Ordinal)) { throw new GraphReadException("UnsafeGraphLink"); }
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(cancellationToken));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect)
            {
                var target = ValidateReportLocation(response.Headers.Location);
                if (probe) { return null; }
                using var download = new HttpRequestMessage(HttpMethod.Get, target);
                using var file = await http.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!file.IsSuccessStatusCode) { throw new GraphReadException("ReportDownloadFailed"); }
                return await ReadBoundedAsync(file, maxBytes, cancellationToken);
            }
            if (response.IsSuccessStatusCode) { return probe ? null : await ReadBoundedAsync(response, maxBytes, cancellationToken); }
            if (await RetryAsync(response, attempt, cancellationToken)) { continue; }
            throw new GraphReadException(Code(response.StatusCode));
        }
        throw new GraphReadException("GraphReadFailed");
    }

    /// <summary>Accepts only the documented HTTPS Microsoft 365 reports download host family for the public cloud.</summary>
    public static Uri ValidateReportLocation(Uri? location)
    {
        if (location is null || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps || !location.IsDefaultPort
            || location.UserInfo.Length != 0 || location.Fragment.Length != 0 || location.AbsoluteUri.Length > 4096
            || !System.Text.RegularExpressions.Regex.IsMatch(location.Host, "^reports[a-z0-9-]{0,20}\\.office\\.com$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new GraphReadException("ReportDownloadHostRejected");
        }
        return location;
    }

    private async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, long maxBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maxBytes) { throw new GraphReadException("ReportFileTooLarge"); }
        await using var stream = new LimitedReadStream(await response.Content.ReadAsStreamAsync(cancellationToken), maxBytes);
        using var buffer = new MemoryStream();
        try { await stream.CopyToAsync(buffer, cancellationToken); }
        catch (ContractException error) when (error.Code == "SizeLimit") { throw new GraphReadException("ReportFileTooLarge"); }
        receivedBytes += buffer.Length;
        return buffer.ToArray();
    }

    private async Task<JsonDocument?> SendAsync(string path, bool optional, CancellationToken cancellationToken)
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
            if (await RetryAsync(response, attempt, cancellationToken)) { continue; }
            if (optional && response.StatusCode == HttpStatusCode.NotFound) { return null; }
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
            throw new GraphReadException(Code(response.StatusCode));
        }
        throw new GraphReadException("GraphReadFailed");
    }

    private async Task<bool> RetryAsync(HttpResponseMessage response, int attempt, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)) { return false; }
        if (attempt == 3) { throw new GraphReadException("Throttled"); }
        var wait = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
            ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
        if (wait > TimeSpan.FromSeconds(60)) { throw new GraphReadException("RetryAfterLimit"); }
        wait = TimeSpan.FromSeconds(Math.Max(wait.TotalSeconds, 0));
        await (delay?.Invoke(wait, cancellationToken) ?? Task.Delay(wait, cancellationToken));
        return true;
    }

    private static string Code(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "AuthenticationRequired",
        HttpStatusCode.Forbidden => "MissingPermission",
        HttpStatusCode.Gone => "CursorExpired",
        _ => "GraphReadFailed"
    };

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
