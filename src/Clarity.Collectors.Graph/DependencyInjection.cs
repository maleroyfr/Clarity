using Clarity.Collectors.Contracts;
using Clarity.Collectors.Graph.Auth;
using Clarity.Collectors.Graph.Entra;
using Clarity.Collectors.Graph.Intune;
using Clarity.Collectors.Graph.Teams;
using Clarity.Collectors.Graph.Tenant;
using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Collectors.Graph;

public static class DependencyInjection
{
    public static IServiceCollection AddGraphCollectors(this IServiceCollection services)
    {
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
        services.AddSingleton<ICollectorCatalog, CollectorCatalog>();

        // Entra fetchers
        services.AddSingleton<IGraphUserFetcher, GraphUserFetcher>();
        services.AddSingleton<IGraphGroupFetcher, GraphGroupFetcher>();
        services.AddSingleton<IGraphRoleFetcher, GraphRoleFetcher>();
        services.AddSingleton<IGraphApplicationFetcher, GraphApplicationFetcher>();
        services.AddSingleton<IGraphServicePrincipalFetcher, GraphServicePrincipalFetcher>();
        services.AddSingleton<IGraphDeviceFetcher, GraphDeviceFetcher>();
        services.AddSingleton<IGraphConditionalAccessFetcher, GraphConditionalAccessFetcher>();

        // Intune fetchers
        services.AddSingleton<IGraphManagedDeviceFetcher, GraphManagedDeviceFetcher>();
        services.AddSingleton<IGraphCompliancePolicyFetcher, GraphCompliancePolicyFetcher>();

        // Teams fetchers
        services.AddSingleton<IGraphTeamFetcher, GraphTeamFetcher>();

        // Tenant fetchers
        services.AddSingleton<IGraphOrganizationFetcher, GraphOrganizationFetcher>();
        services.AddSingleton<IGraphSubscribedSkuFetcher, GraphSubscribedSkuFetcher>();

        // Entra collectors
        services.AddTransient<ICollector, EntraUsersCollector>();
        services.AddTransient<ICollector, EntraGroupsCollector>();
        services.AddTransient<ICollector, EntraRolesCollector>();
        services.AddTransient<ICollector, EntraApplicationsCollector>();
        services.AddTransient<ICollector, EntraServicePrincipalsCollector>();
        services.AddTransient<ICollector, EntraDevicesCollector>();
        services.AddTransient<ICollector, EntraConditionalAccessCollector>();

        // Intune collectors
        services.AddTransient<ICollector, IntuneManagedDevicesCollector>();
        services.AddTransient<ICollector, IntuneCompliancePoliciesCollector>();

        // Teams collectors
        services.AddTransient<ICollector, TeamsCollector>();

        // Tenant collectors
        services.AddTransient<ICollector, TenantOrganizationCollector>();
        services.AddTransient<ICollector, SubscribedSkusCollector>();

        services.AddTransient<ICollectorRunOrchestrator, CollectorRunOrchestrator>();

        return services;
    }
}
