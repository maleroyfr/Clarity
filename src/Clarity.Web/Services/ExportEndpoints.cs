using Clarity.Contracts;
using Clarity.Core;
using Clarity.Infrastructure;
using Microsoft.Extensions.Localization;

namespace Clarity.Web.Services;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/projects/{projectId:guid}/exports").RequireAuthorization();
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
        api.MapGet("/", (Guid projectId, EnvironmentRole? side, int? start, int? count, ExportService service, CancellationToken ct) =>
            service.PageAsync(projectId, side ?? EnvironmentRole.Source, new PageRequest(start ?? 0, count ?? 50), ct));
        api.MapPost("/", (Guid projectId, ExportRequest request, ExportService service, CancellationToken ct) =>
            service.LaunchAsync(projectId, request, ct));
        api.MapGet("/{exportId:guid}/artifacts/{artifactId:guid}", async (Guid projectId, Guid exportId, Guid artifactId, ExportService service,
            CancellationToken ct) =>
        {
            var file = await service.DownloadAsync(projectId, exportId, artifactId, ct);
            return Results.Stream(file.Content, file.ContentType, file.FileName);
        });
    }
}

/// <summary>Resolves export labels from the repository UI resources so generated files follow the fr-FR interface.</summary>
public sealed class ResourceExportText(IStringLocalizer<UiStrings> localizer) : IExportText
{
    public string this[string key]
    {
        get
        {
            var value = localizer[key];
            return value.ResourceNotFound ? key : value.Value;
        }
    }
}
