using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Infrastructure;

public static class PersistenceRegistration
{
    public static IServiceCollection AddClaritySql(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<ClarityDbContext>(
            options => options.UseSqlServer(connectionString), ServiceLifetime.Scoped);
        return services;
    }
}
