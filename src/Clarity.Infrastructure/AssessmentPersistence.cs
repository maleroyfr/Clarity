using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<Assessment> Assessments => Set<Assessment>();

    private void ConfigureAssessments(ModelBuilder model)
    {
        var assessment = DiscoveryOwned<Assessment>(model);
        assessment.Property(x => x.Reason).HasMaxLength(1000);
        assessment.Property(x => x.EvidenceReference).HasMaxLength(1000);
        assessment.Property(x => x.MetadataFingerprint).HasMaxLength(64).IsUnicode(false);
        assessment.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.ObjectId, x.Kind, x.Revision }).IsUnique();
        assessment.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId, x.EnvironmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        assessment.HasOne<InventoryObject>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EnvironmentId, x.ObjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.EnvironmentId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        assessment.HasOne<ObjectObservation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ObjectId, x.EvidenceObservationId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.ObjectId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
