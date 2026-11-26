using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<InventoryObject> InventoryObjects => Set<InventoryObject>();
    public DbSet<ExternalKey> ExternalKeys => Set<ExternalKey>();
    public DbSet<ObjectObservation> ObjectObservations => Set<ObjectObservation>();
    public DbSet<RelationshipObservation> RelationshipObservations => Set<RelationshipObservation>();

    private void ConfigureObservations(ModelBuilder model)
    {
        var item = DiscoveryOwned<InventoryObject>(model);
        item.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId })
            .OnDelete(DeleteBehavior.NoAction);
        item.HasAlternateKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id });
        item.Property(x => x.Type).HasMaxLength(100);

        var key = DiscoveryOwned<ExternalKey>(model);
        key.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        key.Property(x => x.Type).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        key.Property(x => x.Namespace).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        key.Property(x => x.ExternalId).HasMaxLength(500).UseCollation("Latin1_General_100_BIN2");
        key.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Namespace, x.Type, x.ExternalId }).IsUnique();

        var observation = DiscoveryOwned<ObjectObservation>(model);
        observation.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ObjectId })
            .OnDelete(DeleteBehavior.NoAction);
        observation.HasOne<SectionRun>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SectionRunId })
            .OnDelete(DeleteBehavior.NoAction);
        observation.Property(x => x.UserPrincipalName).HasMaxLength(500);
        observation.Property(x => x.Smtp).HasMaxLength(500);
        observation.Property(x => x.Status).HasMaxLength(100);
        observation.Property(x => x.DisplayName).HasMaxLength(500);
        observation.HasIndex(x => new { x.WorkspaceId, x.SectionRunId, x.ObjectId }).IsUnique();
        observation.HasIndex(x => new { x.WorkspaceId, x.UserPrincipalName });
        observation.HasIndex(x => new { x.WorkspaceId, x.Smtp });
        observation.HasIndex(x => new { x.WorkspaceId, x.Status, x.ObservedAtUtc });
        observation.HasIndex(x => new { x.WorkspaceId, x.ObjectId, x.ObservedAtUtc });

        var relation = DiscoveryOwned<RelationshipObservation>(model);
        relation.HasOne<SectionRun>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SectionRunId })
            .OnDelete(DeleteBehavior.NoAction);
        relation.Property(x => x.RelationType).HasMaxLength(100);
        relation.Property(x => x.IdentityHash).HasMaxLength(64).IsUnicode(false);
        relation.HasIndex(x => new { x.WorkspaceId, x.SectionRunId, x.IdentityHash }).IsUnique();
    }
}
