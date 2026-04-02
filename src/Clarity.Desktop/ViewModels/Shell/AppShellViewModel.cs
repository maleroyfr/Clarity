using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Customers;
using Clarity.Desktop.ViewModels.Environments;
using Clarity.Desktop.ViewModels.Inventory;

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
    private bool _isDarkMode;

    public string ThemeIcon => IsDarkMode ? "☀️" : "🌙";

    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new NavItem { Label = "Home",         Icon = "🏠", Section = NavSection.Home },
        new NavItem { Label = "Customers",    Icon = "🏢", Section = NavSection.Customers },
        new NavItem { Label = "Environments", Icon = "🌐", Section = NavSection.Environments },
        new NavItem { Label = "Inventory",    Icon = "📦", Section = NavSection.Inventory },
        new NavItem { Label = "Settings",     Icon = "⚙",  Section = NavSection.Settings }
    ];

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
            NavSection.Inventory    => AppServiceLocator.Get<InventoryExplorerViewModel>(),
            NavSection.Home         => new HomeViewModel(),
            NavSection.Settings     => new SettingsViewModel(),
            _                       => new HomeViewModel()
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
}
