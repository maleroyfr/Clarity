using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<ExportArtifact> ExportArtifacts => Set<ExportArtifact>();

    private void ConfigureExports(ModelBuilder model)
    {
        var export = DiscoveryOwned<ExportJob>(model);
        export.Property(x => x.FiltersJson).HasMaxLength(4000);
        export.Property(x => x.SelectionHash).HasMaxLength(64).IsUnicode(false);
        export.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.Side, x.AsOfUtc });
        export.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        export.HasOne<CollectionJob>().WithOne().HasForeignKey<ExportJob>(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);

        var artifact = DiscoveryOwned<ExportArtifact>(model);
        artifact.Property(x => x.FileName).HasMaxLength(200);
        artifact.Property(x => x.ContentType).HasMaxLength(100).IsUnicode(false);
        artifact.Property(x => x.BlobName).HasMaxLength(200).IsUnicode(false);
        artifact.Property(x => x.ContentHash).HasMaxLength(64).IsUnicode(false);
        artifact.HasIndex(x => new { x.WorkspaceId, x.ExportJobId, x.Kind }).IsUnique();
        artifact.HasOne<ExportJob>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ExportJobId }).OnDelete(DeleteBehavior.NoAction);
    }
}
