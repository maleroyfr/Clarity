using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;

namespace Clarity.Web.Services;

public static class DiscoveryEndpoints
{
    public static void MapDiscoveryEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/projects/{projectId:guid}/discovery").RequireAuthorization();
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                await context.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
                    .ValidateRequestAsync(context.HttpContext);
            }
            return await next(context);
        });
        api.MapGet("/{environmentId:guid}/runs", (Guid projectId, Guid environmentId, DiscoveryQueries queries, CancellationToken ct) =>
            queries.RunsAsync(projectId, environmentId, new(), ct));
        api.MapGet("/{environmentId:guid}/coverage", (Guid projectId, Guid environmentId, DiscoveryQueries queries, CancellationToken ct) =>
            queries.CoverageAsync(projectId, environmentId, new(), ct));
        api.MapGet("/{environmentId:guid}/requests", (Guid projectId, Guid environmentId, DiscoveryQueries queries, CancellationToken ct) =>
            queries.RequestsAsync(projectId, environmentId, new(), ct));
        api.MapGet("/{environmentId:guid}/connection", (Guid projectId, Guid environmentId, DiscoveryQueries queries, CancellationToken ct) =>
            queries.ConnectionAsync(projectId, environmentId, ct));
        api.MapPost("/{environmentId:guid}/connection", (Guid projectId, Guid environmentId, ConnectionInput input, GraphDiscoveryService service, CancellationToken ct) =>
            service.ConnectAsync(projectId, environmentId, input.Name, ct));
        api.MapPost("/{environmentId:guid}/connection/{connectionId:guid}/test", (Guid projectId, Guid environmentId, Guid connectionId, GraphDiscoveryService service, CancellationToken ct) =>
            service.TestAsync(projectId, environmentId, connectionId, ct));
        api.MapPost("/{environmentId:guid}/graph", (Guid projectId, Guid environmentId, GraphInput input, GraphDiscoveryService service, CancellationToken ct) =>
            service.LaunchAsync(projectId, environmentId, input.ConnectionId, input.OperationId, input.Scope, input.Sections, ct));
        api.MapPost("/{environmentId:guid}/requests", (Guid projectId, Guid environmentId, ScriptInput input, ScriptRequestService service, CancellationToken ct) =>
            service.CreateAsync(projectId, environmentId, input.OperationId, input.Scope, input.Sections, input.Domain, ct));
        api.MapPost("/{environmentId:guid}/upload", (Guid projectId, Guid environmentId, HttpContext context, ImportService service, CancellationToken ct) =>
            service.UploadAsync(projectId, environmentId, context.Request.Body, "uploaded-result.zip", ct));
        api.MapPost("/{environmentId:guid}/reports/{profileId}/{version}", (Guid projectId, Guid environmentId, string profileId, string version,
            string scopeKind, string scopeValue, bool attested, HttpContext context, ImportService service, CancellationToken ct) =>
            service.UploadReportAsync(projectId, environmentId, profileId, version, new(scopeKind, scopeValue), attested,
                context.Request.Body, "uploaded-report.csv", ct));
        api.MapGet("/runs/{runId:guid}/preview", async (Guid projectId, Guid runId, ImportService service, CancellationToken ct) =>
            DiscoveryQueries.Present(await service.PreviewAsync(projectId, runId, ct)));
        api.MapPost("/runs/{runId:guid}/confirm", (Guid projectId, Guid runId, ImportService service, CancellationToken ct) =>
            service.ConfirmAsync(projectId, runId, ct));
        api.MapGet("/runs/{runId:guid}/download", async (Guid projectId, Guid runId, ImportService service, CancellationToken ct) =>
        {
            var raw = await service.DownloadRawAsync(projectId, runId, ct);
            return Results.Stream(raw.Content, raw.ContentType, raw.FileName);
        });
        api.MapGet("/requests/{requestId:guid}/parameters", async (Guid projectId, Guid requestId, ScriptRequestService service, CancellationToken ct) =>
            Results.File(await service.DownloadParametersAsync(projectId, requestId, ct), "application/json", "parameters.json"));
        api.MapGet("/requests/{requestId:guid}/bundle", async (Guid projectId, Guid requestId, ScriptRequestService service, CancellationToken ct) =>
            Results.File(await service.DownloadBundleAsync(projectId, requestId, ct), "application/zip", "Clarity.Discovery.AD.1.0.0.zip"));
        api.MapGet("/jobs", (Guid projectId, DiscoveryQueries queries, CancellationToken ct) => queries.JobsAsync(projectId, new(), ct));
        api.MapPost("/jobs/{jobId:guid}/cancel", (Guid projectId, Guid jobId, JobService service, CancellationToken ct) => service.CancelAsync(projectId, jobId, ct));
        api.MapPost("/jobs/{jobId:guid}/retry", (Guid projectId, Guid jobId, JobService service, CancellationToken ct) => service.RetryAsync(projectId, jobId, ct));
    }
}

public sealed record ConnectionInput(string Name);
public sealed record GraphInput(Guid OperationId, Guid ConnectionId, CollectionScope Scope, string[] Sections);
public sealed record ScriptInput(Guid OperationId, CollectionScope Scope, string[] Sections, string? Domain);
