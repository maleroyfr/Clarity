using Clarity.Desktop.ViewModels.Comparisons;
using Clarity.Desktop.ViewModels.Customers;
using Clarity.Desktop.ViewModels.Environments;
using Clarity.Desktop.ViewModels.Exports;
using Clarity.Desktop.ViewModels.Inventory;
using Clarity.Desktop.ViewModels.Onboarding;
using Clarity.Desktop.ViewModels.Relations;
using Clarity.Desktop.ViewModels.Shell;
using Clarity.Desktop.ViewModels.Snapshots;
using Microsoft.Extensions.DependencyInjection;

namespace Clarity.Desktop.Services;

public static class ViewModelRegistration
{
    public static IServiceCollection AddDesktopViewModels(this IServiceCollection services)
    {
        // Shell – singleton so toast/dialog managers are shared
        services.AddSingleton<AppShellViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Customers
        services.AddTransient<CustomersListViewModel>();
        services.AddTransient<CustomerFormViewModel>();

        // Environments
        services.AddTransient<EnvironmentsListViewModel>();
        services.AddTransient<EnvironmentFormViewModel>();
        services.AddTransient<AuthConfigViewModel>();
        services.AddTransient<OnboardingWizardViewModel>();

        // Snapshots & Inventory
        services.AddTransient<SnapshotsViewModel>();
        services.AddTransient<InventoryExplorerViewModel>();
        services.AddTransient<InventoryObjectListViewModel>();

        // Comparisons
        services.AddTransient<ComparisonViewModel>();

        // Exports
        services.AddTransient<ExportsViewModel>();

        // Relations
        services.AddTransient<RelationsViewModel>();

        return services;
    }
}
