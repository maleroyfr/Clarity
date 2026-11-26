using Clarity.Core;

namespace Clarity.Web.Services;

public static class FoundationEndpoints
{
    public static void MapFoundationEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/workspaces", (FoundationService service, CancellationToken ct) => service.ListWorkspacesAsync(new(), ct));
        api.MapPost("/workspaces", (WorkspaceInput input, FoundationService service, CancellationToken ct) => service.CreateWorkspaceAsync(input.OperationId, input.Name, ct));
        api.MapPost("/workspaces/{workspaceId:guid}/archive", (Guid workspaceId, FoundationService service, CancellationToken ct) => service.ArchiveWorkspaceAsync(workspaceId, ct));
        api.MapGet("/workspaces/{workspaceId:guid}/projects", (Guid workspaceId, FoundationService service, CancellationToken ct) => service.ListProjectsAsync(workspaceId, new(), ct));
        api.MapPost("/workspaces/{workspaceId:guid}/projects", (Guid workspaceId, ProjectInput input, FoundationService service, CancellationToken ct) => service.CreateProjectAsync(workspaceId, input.OperationId, input.Name, input.Scenario, input.Description, ct));
        api.MapPost("/workspaces/{workspaceId:guid}/members", (Guid workspaceId, MemberInput input, FoundationService service, CancellationToken ct) => service.SetMemberAsync(workspaceId, new(input.TenantId, input.ObjectId), input.Role, ct));
        api.MapGet("/projects/{projectId:guid}", (Guid projectId, FoundationService service, CancellationToken ct) => service.GetProjectAsync(projectId, ct));
        api.MapPost("/projects/{projectId:guid}/archive", (Guid projectId, FoundationService service, CancellationToken ct) => service.ArchiveProjectAsync(projectId, ct));
        api.MapGet("/projects/{projectId:guid}/environments", (Guid projectId, FoundationService service, CancellationToken ct) => service.ListEnvironmentsAsync(projectId, ct));
        api.MapPost("/projects/{projectId:guid}/environments", (Guid projectId, EnvironmentInput input, FoundationService service, CancellationToken ct) =>
            service.AddEnvironmentAsync(projectId, input.OperationId, input.Role, input.Kind, input.ExternalId, input.DeclaredScope, input.TargetState, input.Hypotheses, ct));
        api.AddEndpointFilter(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                var antiforgery = context.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            return await next(context);
        });
        app.MapGet("/api/antiforgery", (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken })).RequireAuthorization();
    }
}

public sealed record WorkspaceInput(Guid OperationId, string Name);
public sealed record ProjectInput(Guid OperationId, string Name, ProjectScenario Scenario, string Description);
public sealed record EnvironmentInput(Guid OperationId, EnvironmentRole Role, EnvironmentKind Kind, string ExternalId, string DeclaredScope, TargetState TargetState, string Hypotheses);
public sealed record MemberInput(Guid TenantId, Guid ObjectId, WorkspaceRole Role);
