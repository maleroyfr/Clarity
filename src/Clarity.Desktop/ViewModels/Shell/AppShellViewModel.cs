using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Comparisons;
using Clarity.Desktop.ViewModels.Customers;
using Clarity.Desktop.ViewModels.Environments;
using Clarity.Desktop.ViewModels.Exports;
using Clarity.Desktop.ViewModels.Inventory;
using Clarity.Desktop.ViewModels.Relations;
using Clarity.Desktop.ViewModels.Snapshots;
using FluentAvalonia.Styling;

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class AppShellViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private NavSection _activeSection = NavSection.Home;

    public AppShellViewModel()
    {
        NavigateTo(NavSection.Home);
    }

    [RelayCommand]
    public void NavigateTo(NavSection section)
    {
        ActiveSection = section;
        CurrentPage = section switch
        {
            NavSection.Customers    => AppServiceLocator.Get<CustomersListViewModel>(),
            NavSection.Environments => AppServiceLocator.Get<EnvironmentsListViewModel>(),
            NavSection.Snapshots    => AppServiceLocator.Get<SnapshotsViewModel>(),
            NavSection.Inventory    => AppServiceLocator.Get<InventoryExplorerViewModel>(),
            NavSection.Comparisons  => AppServiceLocator.Get<ComparisonViewModel>(),
            NavSection.Exports      => AppServiceLocator.Get<ExportsViewModel>(),
            NavSection.Relations    => AppServiceLocator.Get<RelationsViewModel>(),
            NavSection.Home         => AppServiceLocator.Get<HomeViewModel>(),
            NavSection.Settings     => AppServiceLocator.Get<SettingsViewModel>(),
            _                       => AppServiceLocator.Get<HomeViewModel>()
        };
    }

    public void NavigateToCustomerEnvironments(Guid customerId)
    {
        ActiveSection = NavSection.Environments;
        var vm = AppServiceLocator.Get<EnvironmentsListViewModel>();
        CurrentPage = vm;
        _ = LoadAndSelectCustomerAsync(vm, customerId);
    }

    public void NavigateToEnvironmentSnapshots(Guid customerId, Guid environmentId, string environmentName)
    {
        ActiveSection = NavSection.Snapshots;
        var vm = AppServiceLocator.Get<SnapshotsViewModel>();
        CurrentPage = vm;
        _ = vm.LoadForEnvironmentAsync(customerId, environmentId, environmentName);
    }

    public void NavigateToExportSnapshot(Guid snapshotId, string snapshotName)
    {
        ActiveSection = NavSection.Exports;
        var vm = AppServiceLocator.Get<ExportsViewModel>();
        CurrentPage = vm;
        vm.SetSnapshotContext(snapshotId, snapshotName);
    }

    private static async Task LoadAndSelectCustomerAsync(EnvironmentsListViewModel vm, Guid customerId)
    {
        await vm.LoadCustomersAsync();
        vm.SetCustomer(customerId);
    }
}
