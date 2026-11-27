using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<Population> Populations => Set<Population>();
    public DbSet<PopulationMember> PopulationMembers => Set<PopulationMember>();
    public DbSet<PopulationEntry> PopulationEntries => Set<PopulationEntry>();
    public DbSet<ScopeDecision> ScopeDecisions => Set<ScopeDecision>();
    public DbSet<ScopeDecisionRevision> ScopeDecisionRevisions => Set<ScopeDecisionRevision>();

    private void ConfigureScope(ModelBuilder model)
    {
        var population = DiscoveryOwned<Population>(model);
        population.Property(x => x.Name).HasMaxLength(200);
        population.Property(x => x.Criterion).HasMaxLength(1000);
        population.Property(x => x.ObjectType).HasMaxLength(100);
        population.Property(x => x.Status).HasMaxLength(50);
        population.HasAlternateKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id });
        population.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.CreatedAtUtc });
        population.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        population.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.RootObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        var member = DiscoveryOwned<PopulationMember>(model);
        member.HasIndex(x => new { x.WorkspaceId, x.PopulationId, x.ObjectId }).IsUnique();
        member.HasIndex(x => new { x.WorkspaceId, x.ObjectId });
        member.HasOne<Population>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.PopulationId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        member.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        var entry = DiscoveryOwned<PopulationEntry>(model);
        entry.Property(x => x.Value).HasMaxLength(500);
        entry.HasIndex(x => new { x.WorkspaceId, x.PopulationId, x.Line }).IsUnique();
        entry.HasOne<Population>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.PopulationId }).OnDelete(DeleteBehavior.NoAction);
        entry.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ObjectId }).OnDelete(DeleteBehavior.NoAction);

        var decision = DiscoveryOwned<ScopeDecision>(model);
        decision.Property(x => x.Wave).HasMaxLength(100);
        decision.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false);
        decision.Property(x => x.Revision).IsConcurrencyToken();
        decision.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.ObjectId }).IsUnique();
        decision.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.Value, x.Wave });
        decision.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<Population>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.PopulationId }).OnDelete(DeleteBehavior.NoAction);

        var revision = DiscoveryOwned<ScopeDecisionRevision>(model);
        revision.Property(x => x.Wave).HasMaxLength(100);
        revision.Property(x => x.Reason).HasMaxLength(1000);
        revision.Property(x => x.Fingerprint).HasMaxLength(64).IsUnicode(false);
        revision.HasIndex(x => new { x.WorkspaceId, x.DecisionId, x.Number }).IsUnique();
        revision.HasIndex(x => new { x.WorkspaceId, x.OperationId });
        revision.HasOne<ScopeDecision>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.DecisionId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasOne<Population>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.PopulationId }).OnDelete(DeleteBehavior.NoAction);
        revision.HasOne<ObjectObservation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EvidenceObservationId })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
