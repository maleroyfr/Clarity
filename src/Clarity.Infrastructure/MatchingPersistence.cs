using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<SyncBinding> SyncBindings => Set<SyncBinding>();
    public DbSet<MappingDecision> MappingDecisions => Set<MappingDecision>();
    public DbSet<MappingRevision> MappingRevisions => Set<MappingRevision>();

    private void ConfigureMatching(ModelBuilder model)
    {
        var binding = DiscoveryOwned<SyncBinding>(model);
        binding.Property(x => x.Reason).HasMaxLength(1000);
        binding.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.AdEnvironmentId, x.EntraEnvironmentId }).IsUnique();
        binding.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.AdEnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        binding.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EntraEnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        var decision = DiscoveryOwned<MappingDecision>(model);
        decision.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false);
        decision.Property(x => x.Revision).IsConcurrencyToken();
        decision.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.AdObjectId, x.EntraObjectId }).IsUnique();
        decision.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.State });
        decision.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.AdEnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EntraEnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AdEnvironmentId, x.AdObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EntraEnvironmentId, x.EntraObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        var revision = DiscoveryOwned<MappingRevision>(model);
        revision.Property(x => x.Reason).HasMaxLength(1000);
        revision.HasOne<MappingDecision>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.DecisionId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasIndex(x => new { x.WorkspaceId, x.DecisionId, x.Number }).IsUnique();
    }
}
