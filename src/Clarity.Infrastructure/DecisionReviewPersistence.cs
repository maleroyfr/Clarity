using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<ReviewFlag> ReviewFlags => Set<ReviewFlag>();

    private void ConfigureDecisionReview(ModelBuilder model)
    {
        var flag = DiscoveryOwned<ReviewFlag>(model);
        flag.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false);
        flag.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.Kind, x.ObjectId }).IsUnique();
        flag.HasIndex(x => new { x.WorkspaceId, x.ObjectId });
        flag.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        flag.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
