using System.Data.Common;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Clarity.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Clarity.Web.Operations;

public static class ReadinessChecks
{
    public static void AddClarityReadiness(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration["Storage:BlobServiceUri"] is { Length: > 0 } endpoint)
        {
            var uri = new Uri(endpoint, UriKind.Absolute);
            if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException("Storage:BlobServiceUri must be an HTTPS endpoint without credentials.");
            }
            services.AddSingleton(new BlobServiceClient(uri,
                new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = configuration["AZURE_CLIENT_ID"] })));
        }
        services.AddHealthChecks()
            .AddCheck<SqlReadinessCheck>("sql", timeout: TimeSpan.FromSeconds(10))
            .AddCheck<BlobReadinessCheck>("blob", timeout: TimeSpan.FromSeconds(10));
    }

    public static void MapClarityHealth(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteAsync
        }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = WriteAsync }).AllowAnonymous();
    }

    private static Task WriteAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString())
        }, context.RequestAborted);
}

public sealed class SqlReadinessCheck(IDbContextFactory<ClarityDbContext> factory, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Clarity")))
        {
            return HealthCheckResult.Unhealthy("SQL is not configured.");
        }
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("SQL is unavailable.");
        }
        catch (Exception exception) when (exception is DbException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQL is unavailable.");
        }
    }
}

public sealed class BlobReadinessCheck(BlobServiceClient? blobs = null) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (blobs is null) { return HealthCheckResult.Unhealthy("Blob Storage is not configured."); }
        try
        {
            foreach (var name in new[] { "raw-artifacts", "exports", "script-packages" })
            {
                await blobs.GetBlobContainerClient(name).GetPropertiesAsync(cancellationToken: cancellationToken);
            }
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is RequestFailedException or AuthenticationFailedException or OperationCanceledException or HttpRequestException)
        {
            return HealthCheckResult.Unhealthy("Blob Storage is unavailable.");
        }
    }
}
