using Clarity.Desktop.ViewModels.Customers;
using Clarity.Desktop.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Desktop.Services;

public static class ViewModelRegistration
{
    public static IServiceCollection AddDesktopViewModels(this IServiceCollection services)
    {
        services.AddTransient<AppShellViewModel>();
        services.AddTransient<CustomersListViewModel>();
        services.AddTransient<CustomerFormViewModel>();
        return services;
    }
}
