using Clarity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clarity.Infrastructure;

/// <summary>
/// Used by `dotnet ef` tooling at design time to create a DbContext
/// without needing the full host. Targets SQLite for local development.
/// </summary>
internal sealed class ClarityDbContextFactory : IDesignTimeDbContextFactory<ClarityDbContext>
{
    public ClarityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ClarityDbContext>();
        optionsBuilder.UseSqlite("Data Source=clarity-designtime.db");
        return new ClarityDbContext(optionsBuilder.Options);
    }
}
