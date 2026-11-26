using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed class ClarityDbContext(DbContextOptions<ClarityDbContext> options, IWorkspaceContext scope)
    : DbContext(options)
{
    public DbSet<ClientWorkspace> Workspaces => Set<ClientWorkspace>();
    public DbSet<WorkspaceMember> Members => Set<WorkspaceMember>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectEnvironment> Environments => Set<ProjectEnvironment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var workspace = modelBuilder.Entity<ClientWorkspace>();
        workspace.ToTable("ClientWorkspaces");
        workspace.HasKey(x => x.Id);
        workspace.Property(x => x.Name).HasMaxLength(200);
        workspace.HasIndex(x => new { x.Status, x.Name });
        workspace.HasQueryFilter(x => scope.IsPlatformAdmin || scope.WorkspaceIds.Contains(x.Id));

        var member = modelBuilder.Entity<WorkspaceMember>();
        Owned(member);
        member.HasIndex(x => new { x.WorkspaceId, x.TenantId, x.ObjectId }).IsUnique();
        member.HasIndex(x => new { x.TenantId, x.ObjectId });
        member.HasQueryFilter(x => scope.IsPlatformAdmin || scope.WorkspaceIds.Contains(x.WorkspaceId)
            || (x.TenantId == scope.Identity.TenantId && x.ObjectId == scope.Identity.ObjectId));

        var admin = modelBuilder.Entity<PlatformAdmin>();
        admin.HasKey(x => new { x.TenantId, x.ObjectId });
        admin.HasQueryFilter(x => x.TenantId == scope.Identity.TenantId && x.ObjectId == scope.Identity.ObjectId);

        var project = modelBuilder.Entity<Project>();
        Owned(project);
        project.Property(x => x.Name).HasMaxLength(200);
        project.Property(x => x.Description).HasMaxLength(4000);
        project.Property(x => x.ScopingParameters).HasMaxLength(8000);
        project.HasIndex(x => new { x.WorkspaceId, x.Status, x.Name });
        project.HasQueryFilter(x => scope.WorkspaceIds.Contains(x.WorkspaceId));

        var environment = modelBuilder.Entity<ProjectEnvironment>();
        Owned(environment);
        environment.Property(x => x.ExternalId).HasMaxLength(500);
        environment.Property(x => x.DeclaredScope).HasMaxLength(4000);
        environment.Property(x => x.Hypotheses).HasMaxLength(4000);
        environment.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        environment.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.Role });
        environment.HasQueryFilter(x => scope.WorkspaceIds.Contains(x.WorkspaceId));

        var audit = modelBuilder.Entity<AuditEvent>();
        Owned(audit);
        audit.Property(x => x.Action).HasMaxLength(100);
        audit.Property(x => x.CorrelationId).HasMaxLength(100);
        audit.HasIndex(x => new { x.WorkspaceId, x.OccurredAtUtc });
        audit.HasQueryFilter(x => scope.WorkspaceIds.Contains(x.WorkspaceId));
    }

    private static void Owned<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : WorkspaceEntity
    {
        entity.HasKey(x => new { x.WorkspaceId, x.Id });
        entity.HasOne<ClientWorkspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.NoAction);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateChanges()
    {
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            ModelRules.Validate(entry.Entity);
        }
    }
}
