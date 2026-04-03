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

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class AppShellViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private NavSection _activeSection = NavSection.Home;

    [ObservableProperty]
    private bool _isNavExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeIcon))]
    [NotifyPropertyChangedFor(nameof(ThemeLabel))]
    private bool _isDarkMode;

    public string ThemeIcon => IsDarkMode ? "☀️" : "🌙";
    public string ThemeLabel => IsDarkMode ? "Light Mode" : "Dark Mode";

    public IReadOnlyList<NavGroup> NavGroups { get; } =
    [
        new NavGroup
        {
            Items =
            [
                new NavItem { Label = "Home", Icon = "🏠", Section = NavSection.Home }
            ]
        },
        new NavGroup
        {
            Header = "MANAGE",
            Items =
            [
                new NavItem { Label = "Customers",    Icon = "🏢", Section = NavSection.Customers },
                new NavItem { Label = "Environments", Icon = "🌐", Section = NavSection.Environments },
                new NavItem { Label = "Relations",    Icon = "🔗", Section = NavSection.Relations }
            ]
        },
        new NavGroup
        {
            Header = "DISCOVER",
            Items =
            [
                new NavItem { Label = "Snapshots",  Icon = "📸", Section = NavSection.Snapshots },
                new NavItem { Label = "Inventory",  Icon = "📦", Section = NavSection.Inventory }
            ]
        },
        new NavGroup
        {
            Header = "ANALYZE",
            Items =
            [
                new NavItem { Label = "Comparisons", Icon = "⚖️", Section = NavSection.Comparisons },
                new NavItem { Label = "Exports",     Icon = "📤", Section = NavSection.Exports }
            ]
        },
        new NavGroup
        {
            Items =
            [
                new NavItem { Label = "Settings", Icon = "⚙", Section = NavSection.Settings }
            ]
        }
    ];

    public AppShellViewModel()
    {
        NavigateTo(NavSection.Home);
    }

    [RelayCommand]
    public void NavigateTo(NavSection section)
    {
        ActiveSection = section;
        foreach (var group in NavGroups)
            foreach (var item in group.Items)
                item.IsActive = item.Section == section;

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

    [RelayCommand]
    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        if (Avalonia.Application.Current is not null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = IsDarkMode
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
        }
    }

    [RelayCommand]
    public void ToggleNav() => IsNavExpanded = !IsNavExpanded;

    public void NavigateToCustomerEnvironments(Guid customerId)
    {
        ActiveSection = NavSection.Environments;
        foreach (var group in NavGroups)
            foreach (var item in group.Items)
                item.IsActive = item.Section == NavSection.Environments;

        var vm = AppServiceLocator.Get<EnvironmentsListViewModel>();
        CurrentPage = vm;
        _ = LoadAndSelectCustomerAsync(vm, customerId);
    }

    public void NavigateToEnvironmentSnapshots(Guid customerId, Guid environmentId, string environmentName)
    {
        ActiveSection = NavSection.Snapshots;
        foreach (var group in NavGroups)
            foreach (var item in group.Items)
                item.IsActive = item.Section == NavSection.Snapshots;

        var vm = AppServiceLocator.Get<SnapshotsViewModel>();
        CurrentPage = vm;
        _ = vm.LoadForEnvironmentAsync(customerId, environmentId, environmentName);
    }

    public void NavigateToExportSnapshot(Guid snapshotId, string snapshotName)
    {
        ActiveSection = NavSection.Exports;
        foreach (var group in NavGroups)
            foreach (var item in group.Items)
                item.IsActive = item.Section == NavSection.Exports;

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
