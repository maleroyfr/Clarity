using Clarity.Domain.Comparisons;
using Clarity.Domain.Common;
using Clarity.Domain.Customers;
using Clarity.Domain.Exports;
using Clarity.Domain.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace Clarity.Infrastructure.Persistence;

public sealed class ClarityDbContext(DbContextOptions<ClarityDbContext> options) : DbContext(options)
{
    // ─── Customer ─────────────────────────────────────────────────────────────
    public DbSet<Customer> Customers => Set<Customer>();

    // ─── Environments ─────────────────────────────────────────────────────────
    public DbSet<Domain.Environments.Environment> Environments => Set<Domain.Environments.Environment>();
    public DbSet<Domain.Environments.EnvironmentRelation> EnvironmentRelations => Set<Domain.Environments.EnvironmentRelation>();
    public DbSet<Domain.Environments.WorkloadSelection> WorkloadSelections => Set<Domain.Environments.WorkloadSelection>();
    public DbSet<Domain.Environments.AuthConfiguration> AuthConfigurations => Set<Domain.Environments.AuthConfiguration>();

    // ─── Snapshots ────────────────────────────────────────────────────────────
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<CollectorRun> CollectorRuns => Set<CollectorRun>();
    public DbSet<InventoryObject> InventoryObjects => Set<InventoryObject>();

    // ─── Comparisons ──────────────────────────────────────────────────────────
    public DbSet<ComparisonJob> ComparisonJobs => Set<ComparisonJob>();
    public DbSet<ComparisonResult> ComparisonResults => Set<ComparisonResult>();

    // ─── Exports ──────────────────────────────────────────────────────────────
    public DbSet<ExportProfile> ExportProfiles => Set<ExportProfile>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();

    // ─── Cross-cutting ────────────────────────────────────────────────────────
    public DbSet<ConsultantAnnotation> ConsultantAnnotations => Set<ConsultantAnnotation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClarityDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
