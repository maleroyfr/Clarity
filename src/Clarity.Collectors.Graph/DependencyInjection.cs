using Clarity.Collectors.Contracts;
using Clarity.Collectors.Graph.Auth;
using Clarity.Collectors.Graph.Entra;
using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Collectors.Graph;

public static class DependencyInjection
{
    public static IServiceCollection AddGraphCollectors(this IServiceCollection services)
    {
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
        services.AddSingleton<ICollectorCatalog, CollectorCatalog>();

        services.AddSingleton<IGraphUserFetcher, GraphUserFetcher>();
        services.AddSingleton<IGraphGroupFetcher, GraphGroupFetcher>();
        services.AddSingleton<IGraphRoleFetcher, GraphRoleFetcher>();

        services.AddTransient<ICollector, EntraUsersCollector>();
        services.AddTransient<ICollector, EntraGroupsCollector>();
        services.AddTransient<ICollector, EntraRolesCollector>();

        services.AddTransient<ICollectorRunOrchestrator, CollectorRunOrchestrator>();

        return services;
    }
}
