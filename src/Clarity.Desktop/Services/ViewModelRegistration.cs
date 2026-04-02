using Clarity.Desktop.ViewModels.Customers;
using Clarity.Desktop.ViewModels.Environments;
using Clarity.Desktop.ViewModels.Inventory;
using Clarity.Desktop.ViewModels.Onboarding;
using Clarity.Desktop.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Desktop.Services;

public static class ViewModelRegistration
{
    public static IServiceCollection AddDesktopViewModels(this IServiceCollection services)
    {
        services.AddTransient<AppShellViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<CustomersListViewModel>();
        services.AddTransient<CustomerFormViewModel>();
        services.AddTransient<EnvironmentsListViewModel>();
        services.AddTransient<EnvironmentFormViewModel>();
        services.AddTransient<OnboardingWizardViewModel>();
        services.AddTransient<InventoryExplorerViewModel>();
        services.AddTransient<InventoryObjectListViewModel>();
        return services;
    }
}
