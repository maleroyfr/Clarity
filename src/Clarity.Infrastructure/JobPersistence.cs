using Clarity.Core;
using Microsoft.EntityFrameworkCore;

namespace Clarity.Infrastructure;

public sealed partial class ClarityDbContext
{
    public DbSet<CollectionJob> CollectionJobs => Set<CollectionJob>();

    private void ConfigureJobs(ModelBuilder model)
    {
        var job = DiscoveryOwned<CollectionJob>(model);
        job.HasQueryFilter(x => scope is JobDispatchScope || scope.WorkspaceIds.Contains(x.WorkspaceId));
        job.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
            .OnDelete(DeleteBehavior.NoAction);
        job.Property(x => x.ConcurrencyKey).HasMaxLength(64).IsUnicode(false);
        job.Property(x => x.CorrelationId).HasMaxLength(100);
        job.Property(x => x.ErrorCode).HasMaxLength(100);
        job.HasIndex(x => new { x.State, x.AvailableAtUtc, x.LeaseExpiresAtUtc });
        job.HasIndex(x => new { x.ConcurrencyKey, x.State, x.LeaseExpiresAtUtc });
        job.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.CreatedAtUtc });
    }
}

internal sealed record JobDispatchScope : IWorkspaceContext
{
    public UserIdentity Identity => default;
    public IReadOnlyList<Guid> WorkspaceIds => [];
    public bool IsPlatformAdmin => false;
}

public sealed class JobContextFactory(string connectionString, IWorkspaceContext scope) : IDbContextFactory<ClarityDbContext>
{
    public ClarityDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ClarityDbContext>().UseSqlServer(connectionString)
            .AddInterceptors(new WorkspaceConnectionInterceptor(scope)).Options, scope);
    public Task<ClarityDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
}
