using Clarity.Domain.Environments;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Clarity.Infrastructure.Persistence.Configurations;

internal sealed class SnapshotConfiguration : IEntityTypeConfiguration<Snapshot>
{
    public void Configure(EntityTypeBuilder<Snapshot> b)
    {
        b.ToTable("Snapshots");
        b.HasKey(x => x.Id);

        b.Property(x => x.CustomerId).IsRequired();
        b.Property(x => x.EnvironmentId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Status).IsRequired().HasConversion<string>();
        b.Property(x => x.IsImmutable).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();

        // WorkloadScope stored as JSON string
        b.Property<string>("_workloadScopeJson")
            .HasColumnName("WorkloadScopeJson")
            .HasDefaultValue("[]");

        b.Ignore(x => x.WorkloadScope);

        b.HasMany(x => x.CollectorRuns)
            .WithOne()
            .HasForeignKey(r => r.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Ignore(x => x.DomainEvents);

        b.HasIndex(x => x.EnvironmentId);
        b.HasIndex(x => x.CustomerId);
    }
}

internal sealed class CollectorRunConfiguration : IEntityTypeConfiguration<CollectorRun>
{
    public void Configure(EntityTypeBuilder<CollectorRun> b)
    {
        b.ToTable("CollectorRuns");
        b.HasKey(x => x.Id);

        b.Property(x => x.SnapshotId).IsRequired();
        b.Property(x => x.WorkloadArea).IsRequired().HasConversion<string>();
        b.Property(x => x.CollectorType).IsRequired().HasConversion<string>();
        b.Property(x => x.CollectorVersion).IsRequired().HasMaxLength(50);
        b.Property(x => x.StartedAt).IsRequired();
        b.Property(x => x.Status).IsRequired().HasConversion<string>();
        b.Property(x => x.ItemsCollected).IsRequired();

        // Lists serialised as JSON
        b.Property<string>("_permissionsJson").HasColumnName("PermissionsUsedJson").HasDefaultValue("[]");
        b.Property<string>("_commandsJson").HasColumnName("CommandsExecutedJson").HasDefaultValue("[]");
        b.Property<string>("_warningsJson").HasColumnName("WarningsJson").HasDefaultValue("[]");
        b.Property<string>("_errorsJson").HasColumnName("ErrorsJson").HasDefaultValue("[]");

        b.Ignore(x => x.PermissionsUsed);
        b.Ignore(x => x.CommandsExecuted);
        b.Ignore(x => x.Warnings);
        b.Ignore(x => x.Errors);
    }
}

internal sealed class InventoryObjectConfiguration : IEntityTypeConfiguration<InventoryObject>
{
    public void Configure(EntityTypeBuilder<InventoryObject> b)
    {
        b.ToTable("InventoryObjects");
        b.HasKey(x => x.Id);

        b.Property(x => x.CollectorRunId).IsRequired();
        b.Property(x => x.SnapshotId).IsRequired();
        b.Property(x => x.ObjectType).IsRequired().HasConversion<string>();
        b.Property(x => x.ExternalId).IsRequired().HasMaxLength(500);
        b.Property(x => x.DisplayName).HasMaxLength(500);
        b.Property(x => x.RawDataJson);
        b.Property(x => x.CreatedAt).IsRequired();

        b.Property<string>("_propertiesJson").HasColumnName("PropertiesJson").HasDefaultValue("{}");
        b.Ignore(x => x.Properties);

        b.HasIndex(x => new { x.SnapshotId, x.ObjectType });
        b.HasIndex(x => new { x.ExternalId, x.ObjectType });
    }
}
