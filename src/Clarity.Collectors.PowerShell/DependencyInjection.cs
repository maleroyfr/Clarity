using Clarity.Collectors.Contracts;
using Clarity.Collectors.PowerShell.Exchange;
using Clarity.Collectors.PowerShell.SharePoint;
using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Collectors.PowerShell;

public static class DependencyInjection
{
    public static IServiceCollection AddPowerShellCollectors(this IServiceCollection services)
    {
        services.AddSingleton<IPwshRunner, PwshRunner>();
        services.AddSingleton<IPowerShellPrerequisiteService, PowerShellPrerequisiteService>();
        services.AddTransient<ICollector, ExchangeOnlineCollector>();
        services.AddTransient<ICollector, SharePointSiteCollector>();
        return services;
    }
}
