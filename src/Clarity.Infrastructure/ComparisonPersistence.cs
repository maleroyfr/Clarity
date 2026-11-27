using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<RunComparison> RunComparisons => Set<RunComparison>();
    public DbSet<RunComparisonItem> RunComparisonItems => Set<RunComparisonItem>();

    private void ConfigureComparison(ModelBuilder model)
    {
        var comparison = DiscoveryOwned<RunComparison>(model);
        comparison.HasIndex(x => new { x.WorkspaceId, x.OperationId }).IsUnique();
        comparison.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId, x.BaselineRunId, x.LaterRunId }).IsUnique();
        comparison.HasAlternateKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id });
        comparison.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        comparison.HasOne<CollectionRun>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.BaselineRunId }).OnDelete(DeleteBehavior.NoAction);
        comparison.HasOne<CollectionRun>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.LaterRunId }).OnDelete(DeleteBehavior.NoAction);

        var item = DiscoveryOwned<RunComparisonItem>(model);
        item.Property(x => x.Type).HasMaxLength(100);
        item.Property(x => x.DisplayName).HasMaxLength(400);
        item.Property(x => x.ChangedFields).HasMaxLength(1000);
        item.HasIndex(x => new { x.WorkspaceId, x.ComparisonId, x.ObjectId }).IsUnique();
        item.HasIndex(x => new { x.WorkspaceId, x.ComparisonId, x.Change, x.DisplayName });
        item.HasOne<RunComparison>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ComparisonId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        item.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
