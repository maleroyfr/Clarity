using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<InventoryObject> InventoryObjects => Set<InventoryObject>();
    public DbSet<CurrentValue> CurrentValues => Set<CurrentValue>();
    public DbSet<ExternalKey> ExternalKeys => Set<ExternalKey>();
    public DbSet<AlternateKeyPeriod> AlternateKeyPeriods => Set<AlternateKeyPeriod>();
    public DbSet<ObjectObservation> ObjectObservations => Set<ObjectObservation>();
    public DbSet<RelationshipObservation> RelationshipObservations => Set<RelationshipObservation>();
    public DbSet<InventoryRelationship> InventoryRelationships => Set<InventoryRelationship>();
    public DbSet<RelationshipSupport> RelationshipSupports => Set<RelationshipSupport>();

    private void ConfigureObservations(ModelBuilder model)
    {
        var item = DiscoveryOwned<InventoryObject>(model);
        item.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId })
            .OnDelete(DeleteBehavior.NoAction);
        item.HasAlternateKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id });
        item.Property(x => x.Type).HasMaxLength(100);
        item.Property(x => x.DisplayName).HasMaxLength(500);
        item.Property(x => x.UserPrincipalName).HasMaxLength(500);
        item.Property(x => x.Smtp).HasMaxLength(500);
        item.Property(x => x.SearchName).HasMaxLength(500).HasComputedColumnSql("UPPER([DisplayName])", stored: true);
        item.Property(x => x.Domain).HasMaxLength(500);
        item.Property(x => x.Site).HasMaxLength(500);
        item.HasIndex(x => new { x.WorkspaceId, x.SearchName });
        item.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Domain });
        item.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Site });
        item.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.LastChangedAtUtc });
        item.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Presence, x.Type, x.DisplayName });
        item.HasOne<SectionRun>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.PresenceSectionId })
            .OnDelete(DeleteBehavior.NoAction);

        var current = DiscoveryOwned<CurrentValue>(model);
        current.Property(x => x.Field).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        current.HasIndex(x => new { x.WorkspaceId, x.ObjectId, x.Field }).IsUnique();
        current.HasOne<ObjectObservation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ObjectId, x.ObservationId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ObjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        var key = DiscoveryOwned<ExternalKey>(model);
        key.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        key.Property(x => x.Type).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        key.Property(x => x.Namespace).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        key.Property(x => x.ExternalId).HasMaxLength(500).UseCollation("Latin1_General_100_BIN2");
        key.Property(x => x.IsPrimary).HasDefaultValue(true);
        key.Property(x => x.SearchId).HasMaxLength(500).HasComputedColumnSql("UPPER([ExternalId])", stored: true);
        key.HasIndex(x => new { x.WorkspaceId, x.SearchId });
        key.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Namespace, x.Type, x.ExternalId }).IsUnique()
            .HasFilter("[IsPrimary] = 1");

        var alias = DiscoveryOwned<AlternateKeyPeriod>(model);
        alias.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        alias.HasOne<ObjectObservation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ObjectId, x.ObservationId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ObjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        alias.Property(x => x.Namespace).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        alias.Property(x => x.Type).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        alias.Property(x => x.NormalizedValue).HasMaxLength(500).UseCollation("Latin1_General_100_BIN2");
        alias.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.Namespace, x.Type, x.NormalizedValue });
        alias.HasIndex(x => new { x.WorkspaceId, x.NormalizedValue });

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

        var edge = DiscoveryOwned<InventoryRelationship>(model);
        edge.Property(x => x.RelationType).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        edge.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId }).OnDelete(DeleteBehavior.NoAction);
        edge.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.FromObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        edge.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ToObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        edge.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.FromObjectId, x.Origin, x.IsCurrent });
        edge.HasIndex(x => new { x.WorkspaceId, x.EnvironmentId, x.ToObjectId, x.Origin, x.IsCurrent });
        var support = DiscoveryOwned<RelationshipSupport>(model);
        support.HasOne<InventoryRelationship>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.RelationshipId }).OnDelete(DeleteBehavior.NoAction);
        support.HasOne<RelationshipObservation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ObservationId }).OnDelete(DeleteBehavior.NoAction);
        support.HasIndex(x => new { x.WorkspaceId, x.ObservationId }).IsUnique();
    }
}
