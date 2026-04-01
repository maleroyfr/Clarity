using Clarity.Domain.Comparisons;
using Clarity.Domain.Common;
using Clarity.Domain.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clarity.Infrastructure.Persistence.Configurations;

internal sealed class ComparisonJobConfiguration : IEntityTypeConfiguration<ComparisonJob>
{
    public void Configure(EntityTypeBuilder<ComparisonJob> b)
    {
        b.ToTable("ComparisonJobs");
        b.HasKey(x => x.Id);

        b.Property(x => x.CustomerId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Mode).IsRequired().HasConversion<string>();
        b.Property(x => x.LeftSnapshotId).IsRequired();
        b.Property(x => x.RightSnapshotId).IsRequired();
        b.Property(x => x.Status).IsRequired().HasConversion<string>();
        b.Property(x => x.CreatedAt).IsRequired();

        b.Property<string>("_workloadFilterJson").HasColumnName("WorkloadFilterJson").HasDefaultValue("[]");
        b.Property<string>("_objectTypeFilterJson").HasColumnName("ObjectTypeFilterJson").HasDefaultValue("[]");

        b.Ignore(x => x.WorkloadFilter);
        b.Ignore(x => x.ObjectTypeFilter);

        b.HasMany(x => x.Results)
            .WithOne()
            .HasForeignKey(r => r.ComparisonJobId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class ComparisonResultConfiguration : IEntityTypeConfiguration<ComparisonResult>
{
    public void Configure(EntityTypeBuilder<ComparisonResult> b)
    {
        b.ToTable("ComparisonResults");
        b.HasKey(x => x.Id);

        b.Property(x => x.ComparisonJobId).IsRequired();
        b.Property(x => x.WorkloadArea).IsRequired().HasConversion<string>();
        b.Property(x => x.TotalAdded).IsRequired();
        b.Property(x => x.TotalRemoved).IsRequired();
        b.Property(x => x.TotalModified).IsRequired();
        b.Property(x => x.TotalUnchanged).IsRequired();

        // DeltaItems stored as JSON for query simplicity at this stage
        b.Property<string>("_deltaItemsJson").HasColumnName("DeltaItemsJson").HasDefaultValue("[]");
        b.Ignore(x => x.DeltaItems);
    }
}

internal sealed class ExportProfileConfiguration : IEntityTypeConfiguration<ExportProfile>
{
    public void Configure(EntityTypeBuilder<ExportProfile> b)
    {
        b.ToTable("ExportProfiles");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.IncludeRawData).IsRequired();
        b.Property(x => x.IncludeMetadata).IsRequired();
        b.Property(x => x.IncludeSummarySheet).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();

        b.Property<string>("_workloadsJson").HasColumnName("IncludedWorkloadsJson").HasDefaultValue("[]");
        b.Property<string>("_objectTypesJson").HasColumnName("IncludedObjectTypesJson").HasDefaultValue("[]");

        b.Ignore(x => x.IncludedWorkloads);
        b.Ignore(x => x.IncludedObjectTypes);
    }
}

internal sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> b)
    {
        b.ToTable("ExportJobs");
        b.HasKey(x => x.Id);

        b.Property(x => x.CustomerId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Format).IsRequired().HasConversion<string>();
        b.Property(x => x.Status).IsRequired().HasConversion<string>();
        b.Property(x => x.CreatedAt).IsRequired();

        b.Property<string>("_snapshotIdsJson").HasColumnName("SnapshotIdsJson").HasDefaultValue("[]");
        b.Ignore(x => x.SnapshotIds);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class ConsultantAnnotationConfiguration : IEntityTypeConfiguration<ConsultantAnnotation>
{
    public void Configure(EntityTypeBuilder<ConsultantAnnotation> b)
    {
        b.ToTable("ConsultantAnnotations");
        b.HasKey(x => x.Id);

        b.Property(x => x.EntityId).IsRequired();
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
        b.Property(x => x.Text).IsRequired();
        b.Property(x => x.Category).HasMaxLength(100);
        b.Property(x => x.CreatedBy).HasMaxLength(200);
        b.Property(x => x.CreatedAt).IsRequired();

        b.HasIndex(x => new { x.EntityId, x.EntityType });
    }
}
