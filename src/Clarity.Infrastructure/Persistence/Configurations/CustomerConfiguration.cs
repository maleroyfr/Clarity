using Clarity.Domain.Common;
using Clarity.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Clarity.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.IsArchived).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.UpdatedAt).IsRequired();

        // Tags stored as JSON column (SQLite compatible)
        b.Property<string>("_tagsJson")
            .HasColumnName("TagsJson")
            .HasDefaultValue("[]");

        // Ignore the navigation property — tags serialized to JSON column via value converter
        b.Ignore(x => x.Tags);
        b.Ignore(x => x.DomainEvents);
    }
}
