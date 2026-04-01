using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Comparisons;

public static class DependencyInjection
{
    public static IServiceCollection AddComparisons(this IServiceCollection services)
    {
        services.AddScoped<IComparisonService, ComparisonService>();
        return services;
    }
}
