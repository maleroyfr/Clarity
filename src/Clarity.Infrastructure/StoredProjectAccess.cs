using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class StoredProjectAccess(IDbContextFactory<ClarityDbContext> factory, IWorkspaceContext scope) : IProjectAccess
{
    public async Task<Project> RequireAsync(Guid projectId, WorkspaceOperation operation, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken)
            ?? throw new AccessDeniedException();
        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId
            && x.TenantId == scope.Identity.TenantId && x.ObjectId == scope.Identity.ObjectId, cancellationToken);
        if (!WorkspacePolicy.Allows(member?.Role, operation)
            || !ProjectLifecycle.Permits(project.Status, operation is WorkspaceOperation.Read or WorkspaceOperation.DeleteProject
                || await db.Workspaces.AnyAsync(x => x.Id == project.WorkspaceId && x.Status == WorkspaceStatus.Active, cancellationToken), operation))
        {
            throw new AccessDeniedException();
        }
        return project;
    }
}
