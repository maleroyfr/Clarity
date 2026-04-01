using Clarity.Domain.Comparisons;
using Clarity.Domain.Customers;
using Clarity.Domain.Environments;
using Clarity.Domain.Exports;
using Clarity.Domain.Snapshots;
using Clarity.Infrastructure.Persistence;
using Clarity.Infrastructure.Onboarding;
using Clarity.Infrastructure.Repositories;
using Clarity.Application.Onboarding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Clarity.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure services.
    /// connectionString = null → SQLite local dev mode.
    /// connectionString = Azure SQL connection string → production mode.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string? sqliteDbPath = null,
        string? sqlServerConnectionString = null)
    {
        services.AddDbContext<ClarityDbContext>(opts =>
        {
            if (!string.IsNullOrWhiteSpace(sqlServerConnectionString))
            {
                opts.UseSqlServer(sqlServerConnectionString, b =>
                    b.MigrationsAssembly(typeof(ClarityDbContext).Assembly.FullName));
            }
            else
            {
                var path = sqliteDbPath ?? Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "Clarity", "clarity.db");

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                opts.UseSqlite($"Data Source={path}", b =>
                    b.MigrationsAssembly(typeof(ClarityDbContext).Assembly.FullName));
            }
        });

        // Repositories
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IEnvironmentRepository, EnvironmentRepository>();
        services.AddScoped<IEnvironmentRelationRepository, EnvironmentRelationRepository>();
        services.AddScoped<ISnapshotRepository, SnapshotRepository>();
        services.AddScoped<IInventoryObjectRepository, InventoryObjectRepository>();
        services.AddScoped<IExportJobRepository, ExportJobRepository>();
        services.AddScoped<IComparisonJobRepository, ComparisonJobRepository>();
        services.AddSingleton<IAzureCliSetupScriptGenerator, AzureCliSetupScriptGenerator>();

        return services;
    }

    /// <summary>Applies pending EF Core migrations on startup (dev / local mode).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClarityDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migration applied successfully.");
    }
}
