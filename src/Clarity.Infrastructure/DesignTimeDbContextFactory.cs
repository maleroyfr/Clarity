using Clarity.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clarity.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ClarityDbContext>
{
    public ClarityDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("CLARITY_MIGRATION_SQL")
            ?? "Server=localhost;Database=Clarity;Integrated Security=True;Encrypt=True";
        var options = new DbContextOptionsBuilder<ClarityDbContext>().UseSqlServer(connection).Options;
        return new ClarityDbContext(options, new WorkspaceScope(default, []));
    }
}
