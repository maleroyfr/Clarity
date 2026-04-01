using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Collectors.PowerShell;

public static class DependencyInjection
{
    public static IServiceCollection AddPowerShellCollectors(this IServiceCollection services)
    {
        services.AddSingleton<IPwshRunner, PwshRunner>();
        services.AddSingleton<IPowerShellPrerequisiteService, PowerShellPrerequisiteService>();
        return services;
    }
}
