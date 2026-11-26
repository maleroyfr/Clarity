using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<CollectionRequest> CollectionRequests => Set<CollectionRequest>();
    public DbSet<CollectionRun> CollectionRuns => Set<CollectionRun>();
    public DbSet<SectionRun> SectionRuns => Set<SectionRun>();
    public DbSet<RawArtifact> RawArtifacts => Set<RawArtifact>();

    private void ConfigureDiscovery(ModelBuilder model)
    {
        var connection = DiscoveryOwned<Connection>(model);
        connection.Property(x => x.Name).HasMaxLength(200);
        connection.Property(x => x.ProducerId).HasMaxLength(100);
        connection.Property(x => x.ConfigurationReference).HasMaxLength(100);
        connection.HasIndex(x => new { x.WorkspaceId, x.TenantId, x.ProducerId }).IsUnique();

        // Include project in environment references, not just workspace, to prevent cross-dossier attachment.
        model.Entity<ProjectEnvironment>().HasAlternateKey(x => new { x.WorkspaceId, x.ProjectId, x.Id });
        var request = DiscoveryOwned<CollectionRequest>(model);
        request.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        request.HasAlternateKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId, x.Id });
        request.Property(x => x.ScopeJson).HasMaxLength(8000);
        request.Property(x => x.ScopeHash).HasMaxLength(64).IsUnicode(false);
        request.Property(x => x.ProducerId).HasMaxLength(100);
        request.Property(x => x.ScriptVersion).HasMaxLength(100);
        request.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.CreatedAtUtc });

        var run = DiscoveryOwned<CollectionRun>(model);
        run.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        run.HasOne<CollectionRequest>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId, x.RequestId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        run.HasOne<Connection>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ConnectionId }).OnDelete(DeleteBehavior.NoAction);
        run.Property(x => x.ProducerId).HasMaxLength(100);
        run.Property(x => x.ProducerVersion).HasMaxLength(100);
        run.Property(x => x.ScopeJson).HasMaxLength(8000);
        run.Property(x => x.ScopeHash).HasMaxLength(64).IsUnicode(false);
        run.Property(x => x.ContentHash).HasMaxLength(64).IsUnicode(false);
        run.Property(x => x.CorrelationId).HasMaxLength(100);
        run.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId, x.ExternalRunId }).IsUnique();
        run.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId, x.ScopeHash, x.ContentHash }).IsUnique();
        run.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.UploadedAtUtc });

        var section = DiscoveryOwned<SectionRun>(model);
        section.HasOne<CollectionRun>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.RunId }).OnDelete(DeleteBehavior.NoAction);
        section.Property(x => x.SectionId).HasMaxLength(200);
        section.Property(x => x.CapabilityId).HasMaxLength(100);
        section.Property(x => x.ScopeJson).HasMaxLength(8000);
        section.Property(x => x.ScopeHash).HasMaxLength(64).IsUnicode(false);
        section.HasIndex(x => new { x.WorkspaceId, x.RunId, x.SectionId }).IsUnique();
        section.HasIndex(x => new { x.WorkspaceId, x.CapabilityId, x.ScopeHash, x.PublishedAtUtc });

        var artifact = DiscoveryOwned<RawArtifact>(model);
        artifact.HasOne<CollectionRun>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.RunId }).OnDelete(DeleteBehavior.NoAction);
        artifact.Property(x => x.BlobName).HasMaxLength(1000);
        artifact.Property(x => x.ContentHash).HasMaxLength(64).IsUnicode(false);
        artifact.Property(x => x.FileName).HasMaxLength(200);
        artifact.HasIndex(x => new { x.WorkspaceId, x.RunId }).IsUnique();
        ConfigureObservations(model);
    }

    private EntityTypeBuilder<TEntity> DiscoveryOwned<TEntity>(ModelBuilder model) where TEntity : WorkspaceEntity
    {
        var entity = model.Entity<TEntity>();
        Owned(entity);
        entity.HasQueryFilter(x => scope.WorkspaceIds.Contains(x.WorkspaceId));
        return entity;
    }
}
