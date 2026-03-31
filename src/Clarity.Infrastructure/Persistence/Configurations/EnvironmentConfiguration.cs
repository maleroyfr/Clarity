using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clarity.Infrastructure.Persistence.Configurations;

internal sealed class EnvironmentConfiguration : IEntityTypeConfiguration<Domain.Environments.Environment>
{
    public void Configure(EntityTypeBuilder<Domain.Environments.Environment> b)
    {
        b.ToTable("Environments");
        b.HasKey(x => x.Id);

        b.Property(x => x.CustomerId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Type).IsRequired().HasConversion<string>();
        b.Property(x => x.TenantDomain).HasMaxLength(200);
        b.Property(x => x.Status).IsRequired().HasConversion<string>();
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.UpdatedAt).IsRequired();

        b.HasMany(x => x.WorkloadSelections)
            .WithOne()
            .HasForeignKey(w => w.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.AuthConfigurations)
            .WithOne()
            .HasForeignKey(a => a.EnvironmentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Ignore(x => x.Tags);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class WorkloadSelectionConfiguration : IEntityTypeConfiguration<WorkloadSelection>
{
    public void Configure(EntityTypeBuilder<WorkloadSelection> b)
    {
        b.ToTable("WorkloadSelections");
        b.HasKey(x => x.Id);

        b.Property(x => x.EnvironmentId).IsRequired();
        b.Property(x => x.WorkloadArea).IsRequired().HasConversion<string>();
        b.Property(x => x.IsEnabled).IsRequired();
        b.Property(x => x.ConfigStatus).IsRequired().HasConversion<string>();
        b.Property(x => x.UpdatedAt).IsRequired();

        b.HasIndex(x => new { x.EnvironmentId, x.WorkloadArea }).IsUnique();
        b.Ignore(x => x.Prerequisites);
    }
}

internal sealed class AuthConfigurationConfiguration : IEntityTypeConfiguration<AuthConfiguration>
{
    public void Configure(EntityTypeBuilder<AuthConfiguration> b)
    {
        b.ToTable("AuthConfigurations");
        b.HasKey(x => x.Id);

        b.Property(x => x.EnvironmentId).IsRequired();
        b.Property(x => x.WorkloadArea).IsRequired().HasConversion<string>();
        b.Property(x => x.AuthType).IsRequired().HasConversion<string>();
        b.Property(x => x.ClientId).HasMaxLength(100);
        b.Property(x => x.TenantId).HasMaxLength(100);
        b.Property(x => x.CertificateThumbprint).HasMaxLength(100);
        b.Property(x => x.SecretReference).HasMaxLength(500);
        b.Property(x => x.IsActive).IsRequired();
    }
}

internal sealed class EnvironmentRelationConfiguration : IEntityTypeConfiguration<EnvironmentRelation>
{
    public void Configure(EntityTypeBuilder<EnvironmentRelation> b)
    {
        b.ToTable("EnvironmentRelations");
        b.HasKey(x => x.Id);

        b.Property(x => x.CustomerId).IsRequired();
        b.Property(x => x.SourceEnvironmentId).IsRequired();
        b.Property(x => x.TargetEnvironmentId).IsRequired();
        b.Property(x => x.RelationType).IsRequired().HasConversion<string>();
        b.Property(x => x.Direction).IsRequired().HasConversion<string>();
        b.Property(x => x.Notes).HasMaxLength(4000);

        b.Ignore(x => x.Tags);
        b.Ignore(x => x.DomainEvents);
    }
}
