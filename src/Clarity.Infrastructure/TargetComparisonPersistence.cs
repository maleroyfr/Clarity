using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<TargetMappingRule> TargetMappingRules => Set<TargetMappingRule>();
    public DbSet<TargetComparison> TargetComparisons => Set<TargetComparison>();
    public DbSet<TargetComparisonItem> TargetComparisonItems => Set<TargetComparisonItem>();

    private void ConfigureTargetComparison(ModelBuilder model)
    {
        var rule = DiscoveryOwned<TargetMappingRule>(model);
        rule.Property(x => x.ObjectType).HasMaxLength(100);
        rule.Property(x => x.SourceField).HasMaxLength(100);
        rule.Property(x => x.TargetField).HasMaxLength(100);
        rule.Property(x => x.TargetDomain).HasMaxLength(253);
        rule.Property(x => x.Justification).HasMaxLength(1000);
        rule.Property(x => x.RetirementReason).HasMaxLength(1000);
        rule.HasIndex(x => new { x.WorkspaceId, x.OperationId }).IsUnique();
        rule.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.RetiredAtUtc });
        rule.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        var comparison = DiscoveryOwned<TargetComparison>(model);
        comparison.HasIndex(x => new { x.WorkspaceId, x.OperationId }).IsUnique();
        comparison.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.SourceEnvironmentId, x.TargetEnvironmentId }).IsUnique();
        comparison.HasAlternateKey(x => new { x.WorkspaceId, x.SourceEnvironmentId, x.TargetEnvironmentId, x.Id });
        comparison.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.SourceEnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        comparison.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.TargetEnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        var item = DiscoveryOwned<TargetComparisonItem>(model);
        item.Property(x => x.ObjectType).HasMaxLength(100);
        item.Property(x => x.Label).HasMaxLength(400);
        item.Property(x => x.TargetLabel).HasMaxLength(400);
        item.Property(x => x.OtherLabel).HasMaxLength(400);
        item.Property(x => x.Key).HasMaxLength(500);
        item.Property(x => x.Reason).HasMaxLength(100);
        item.HasIndex(x => new { x.WorkspaceId, x.ComparisonId, x.Outcome, x.Label });
        item.HasIndex(x => new { x.WorkspaceId, x.ComparisonId, x.Collision, x.Key });
        item.HasOne<TargetComparison>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.SourceEnvironmentId, x.TargetEnvironmentId, x.ComparisonId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.SourceEnvironmentId, x.TargetEnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        item.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SourceEnvironmentId, x.SourceObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        item.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.SourceEnvironmentId, x.OtherObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        item.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.TargetEnvironmentId, x.TargetObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
