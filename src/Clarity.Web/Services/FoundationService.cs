using System.Diagnostics;
using Clarity.Core;
using Clarity.Infrastructure;
using Clarity.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Web.Services;

public sealed class FoundationService(
    IDbContextFactory<ClarityDbContext> factory, WorkspaceAuthorization authorization, IWorkspaceContext scope)
{
    public async Task<PageResult<ClientWorkspace>> ListWorkspacesAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        request.Validate();
        await authorization.RefreshAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.Workspaces.AsNoTracking().Where(x => x.Name.Contains(request.Search));
        var count = await query.CountAsync(cancellationToken);
        var sorted = request.Descending ? query.OrderByDescending(x => x.Name).ThenBy(x => x.Id) : query.OrderBy(x => x.Name).ThenBy(x => x.Id);
        return new(await sorted.Skip(request.Start).Take(request.Count).ToListAsync(cancellationToken), count);
    }

    public async Task<ClientWorkspace> CreateWorkspaceAsync(Guid operationId, string name, CancellationToken cancellationToken = default)
    {
        DemandOperationId(operationId);
        await authorization.RefreshAsync(cancellationToken);
        await authorization.DemandAsync(Guid.Empty, WorkspaceOperation.AdministerPlatform);
        var workspace = new ClientWorkspace { Id = operationId, Name = name.Trim() };
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == operationId, cancellationToken);
        if (existing is not null)
        {
            return existing.Name == workspace.Name ? existing : throw new ConflictException();
        }
        db.Workspaces.Add(workspace);
        AddAudit(db, workspace.Id, "WorkspaceCreated", workspace.Id);
        await db.SaveChangesAsync(cancellationToken);
        return workspace;
    }

    public async Task ArchiveWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await authorization.RefreshAsync(cancellationToken);
        await authorization.DemandAsync(workspaceId, WorkspaceOperation.AdministerPlatform);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var workspace = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == workspaceId, cancellationToken) ?? throw new AccessDeniedException();
        if (workspace.Status == WorkspaceStatus.Archived)
        {
            return;
        }
        workspace.Archive();
        AddAudit(db, workspaceId, "WorkspaceArchived", workspaceId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PageResult<Project>> ListProjectsAsync(Guid workspaceId, PageRequest request, CancellationToken cancellationToken = default)
    {
        request.Validate();
        await authorization.RefreshAsync(cancellationToken);
        await authorization.DemandAsync(workspaceId, WorkspaceOperation.Read);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.Projects.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.Name.Contains(request.Search));
        var count = await query.CountAsync(cancellationToken);
        var sorted = request.Descending ? query.OrderByDescending(x => x.Name).ThenBy(x => x.Id) : query.OrderBy(x => x.Name).ThenBy(x => x.Id);
        return new(await sorted.Skip(request.Start).Take(request.Count).ToListAsync(cancellationToken), count);
    }

    public async Task<Project> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await authorization.RefreshAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken)
            ?? throw new AccessDeniedException();
        await authorization.DemandAsync(project.WorkspaceId, WorkspaceOperation.Read);
        AddAudit(db, project.WorkspaceId, "ProjectRead", project.Id);
        await db.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task<Project> CreateProjectAsync(Guid workspaceId, Guid operationId, string name, ProjectScenario scenario, string description,
        CancellationToken cancellationToken = default)
    {
        DemandOperationId(operationId);
        await authorization.RefreshAsync(cancellationToken);
        await authorization.DemandAsync(workspaceId, WorkspaceOperation.Qualify);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await DemandActiveWorkspaceAsync(db, workspaceId, cancellationToken);
        var project = new Project { Id = operationId, WorkspaceId = workspaceId, Name = name.Trim(), Scenario = scenario, Description = description };
        var existing = await db.Projects.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == operationId, cancellationToken);
        if (existing is not null)
        {
            return existing.Name == project.Name && existing.Scenario == scenario && existing.Description == description
                ? existing : throw new ConflictException();
        }
        db.Projects.Add(project);
        AddAudit(db, workspaceId, "ProjectCreated", project.Id);
        await db.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task ArchiveProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await authorization.RefreshAsync(cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken) ?? throw new AccessDeniedException();
        await authorization.DemandAsync(project.WorkspaceId, WorkspaceOperation.Qualify);
        if (project.Status == ProjectStatus.Archived)
        {
            return;
        }
        project.Archive();
        AddAudit(db, project.WorkspaceId, "ProjectArchived", project.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectEnvironment>> ListEnvironmentsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Environments.AsNoTracking().Where(x => x.WorkspaceId == project.WorkspaceId && x.ProjectId == project.Id)
            .OrderBy(x => x.Role).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task AddEnvironmentAsync(Guid projectId, Guid operationId, EnvironmentRole role, EnvironmentKind kind, string externalId,
        string declaredScope, TargetState targetState, string hypotheses, CancellationToken cancellationToken = default)
    {
        DemandOperationId(operationId);
        var project = await GetProjectAsync(projectId, cancellationToken);
        await authorization.DemandAsync(project.WorkspaceId, WorkspaceOperation.ManageConnections);
        if (project.Status == ProjectStatus.Archived)
        {
            throw new ArgumentException("An archived project cannot be changed.");
        }
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await DemandActiveWorkspaceAsync(db, project.WorkspaceId, cancellationToken);
        var environment = new ProjectEnvironment
        {
            Id = operationId,
            WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id,
            Role = role,
            Kind = kind,
            ExternalId = externalId.Trim(),
            DeclaredScope = declaredScope,
            TargetState = targetState,
            Hypotheses = hypotheses
        };
        var existing = await db.Environments.SingleOrDefaultAsync(x => x.WorkspaceId == project.WorkspaceId && x.Id == operationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ProjectId != projectId || existing.Role != role || existing.Kind != kind || existing.ExternalId != environment.ExternalId
                || existing.DeclaredScope != declaredScope || existing.TargetState != targetState || existing.Hypotheses != hypotheses)
            {
                throw new ConflictException();
            }
            return;
        }
        db.Environments.Add(environment);
        AddAudit(db, project.WorkspaceId, "EnvironmentDeclared", environment.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetMemberAsync(Guid workspaceId, UserIdentity identity, WorkspaceRole role, CancellationToken cancellationToken = default)
    {
        await authorization.RefreshAsync(cancellationToken);
        if (scope.IsPlatformAdmin)
        {
            await authorization.DemandAsync(workspaceId, WorkspaceOperation.AdministerPlatform);
        }
        else
        {
            await authorization.DemandAsync(workspaceId, WorkspaceOperation.ManageMembers);
        }
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await DemandActiveWorkspaceAsync(db, workspaceId, cancellationToken);
        var member = await db.Members.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.TenantId == identity.TenantId && x.ObjectId == identity.ObjectId, cancellationToken);
        if (member is null)
        {
            member = new WorkspaceMember { WorkspaceId = workspaceId, TenantId = identity.TenantId, ObjectId = identity.ObjectId, Role = role };
            db.Members.Add(member);
        }
        else
        {
            if (member.Role == role)
            {
                return;
            }
            member.Role = role;
        }
        AddAudit(db, workspaceId, "WorkspaceMemberAssigned", member.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void DemandOperationId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A stable operation identifier is required for retries.");
        }
    }

    private static async Task DemandActiveWorkspaceAsync(ClarityDbContext db, Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await db.Workspaces.AnyAsync(x => x.Id == workspaceId && x.Status == WorkspaceStatus.Active, cancellationToken))
        {
            throw new AccessDeniedException();
        }
    }

    private void AddAudit(ClarityDbContext db, Guid workspaceId, string action, Guid resourceId) =>
        db.AuditEvents.Add(new AuditEvent
        {
            WorkspaceId = workspaceId,
            ActorTenantId = scope.Identity.TenantId,
            ActorObjectId = scope.Identity.ObjectId,
            Action = action,
            ResourceId = resourceId,
            CorrelationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        });
}
