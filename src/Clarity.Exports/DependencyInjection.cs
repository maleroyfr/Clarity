using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Exports;

public static class DependencyInjection
{
    public static IServiceCollection AddExports(this IServiceCollection services)
    {
        services.AddScoped<IExportService, ExportService>();
        return services;
    }
}
