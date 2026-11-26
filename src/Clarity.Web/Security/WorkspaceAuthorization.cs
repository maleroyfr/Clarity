using System.Security.Claims;
using Clarity.Core;
using Clarity.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Web.Security;

public sealed record WorkspaceRequirement(WorkspaceOperation Operation) : IAuthorizationRequirement;
public sealed record WorkspaceResource(Guid WorkspaceId, WorkspaceRole? Role, bool PlatformAdmin);

public sealed class WorkspaceAuthorizationHandler : AuthorizationHandler<WorkspaceRequirement, WorkspaceResource>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        WorkspaceRequirement requirement, WorkspaceResource resource)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && WorkspacePolicy.Allows(resource.Role, requirement.Operation, resource.PlatformAdmin))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

public sealed class WorkspaceAuthorization(
    WorkspaceContext scope, IDbContextFactory<ClarityDbContext> factory, IHttpContextAccessor httpContext,
    AuthenticationStateProvider authenticationState, IAuthorizationService authorization, IWebHostEnvironment environment) : IDisposable
{
    private IReadOnlyList<WorkspaceMember> _members = [];
    private ClaimsPrincipal _principal = new();
    private bool _subscribed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_subscribed)
        {
            authenticationState.AuthenticationStateChanged += AuthenticationChanged;
            _subscribed = true;
        }
        _principal = httpContext.HttpContext?.User
            ?? (await authenticationState.GetAuthenticationStateAsync()).User;
        if (_principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(_principal.FindFirstValue("tid") ?? _principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid"), out var tenant)
            || !Guid.TryParse(_principal.FindFirstValue("oid") ?? _principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier"), out var subject))
        {
            throw new AccessDeniedException();
        }

        var identity = new UserIdentity(tenant, subject);
        if (scope.Identity != identity)
        {
            scope.WorkspaceIds = [];
            scope.IsPlatformAdmin = false;
        }
        scope.Identity = identity;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        _members = await db.Members.AsNoTracking().Where(x => x.TenantId == tenant && x.ObjectId == subject).ToListAsync(cancellationToken);
        scope.IsPlatformAdmin = await db.PlatformAdmins.AnyAsync(cancellationToken);

        if ((environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            && _principal.Identity.AuthenticationType == DevelopmentAuthenticationHandler.SchemeName)
        {
            var persona = _principal.Identity.Name;
            var role = persona switch
            {
                "manager" or "beta-manager" => WorkspaceRole.WorkspaceManager,
                "consultant" => WorkspaceRole.Consultant,
                "reader" => WorkspaceRole.Reader,
                _ => (WorkspaceRole?)null
            };
            var synthetic = role is null ? Array.Empty<WorkspaceMember>() :
                [new WorkspaceMember
                {
                    WorkspaceId = persona == "beta-manager" ? DevelopmentAuthenticationHandler.BetaWorkspace : DevelopmentAuthenticationHandler.AlphaWorkspace,
                    TenantId = tenant, ObjectId = subject, Role = role.Value
                }];
            _members = _members.Concat(synthetic).DistinctBy(x => x.WorkspaceId).ToArray();
            scope.IsPlatformAdmin = persona == "platform-admin";
        }
        scope.WorkspaceIds = _members.Select(x => x.WorkspaceId).ToArray();
    }

    public async Task DemandAsync(Guid workspaceId, WorkspaceOperation operation)
    {
        var member = _members.FirstOrDefault(x => x.WorkspaceId == workspaceId);
        var resource = new WorkspaceResource(workspaceId, member?.Role, scope.IsPlatformAdmin);
        var result = await authorization.AuthorizeAsync(_principal, resource, [new WorkspaceRequirement(operation)]);
        if (!result.Succeeded)
        {
            throw new AccessDeniedException();
        }

    }

    public bool Allows(Guid workspaceId, WorkspaceOperation operation) => _principal.Identity?.IsAuthenticated == true
        && WorkspacePolicy.Allows(_members.FirstOrDefault(x => x.WorkspaceId == workspaceId)?.Role, operation, scope.IsPlatformAdmin);

    private void AuthenticationChanged(Task<AuthenticationState> state)
    {
        _principal = new();
        _members = [];
        scope.Identity = default;
        scope.WorkspaceIds = [];
        scope.IsPlatformAdmin = false;
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            authenticationState.AuthenticationStateChanged -= AuthenticationChanged;
        }
    }
}
