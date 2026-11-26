using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Infrastructure;

public static class PersistenceRegistration
{
    public static IServiceCollection AddClaritySql(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<ClarityDbContext>(
            (provider, options) => options.UseSqlServer(connectionString)
                .AddInterceptors(new WorkspaceConnectionInterceptor(provider.GetRequiredService<Clarity.Core.IWorkspaceContext>())),
            ServiceLifetime.Scoped);
        return services;
    }
}
