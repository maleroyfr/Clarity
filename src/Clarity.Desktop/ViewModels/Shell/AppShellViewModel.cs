using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Customers;

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class AppShellViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private NavSection _activeSection = NavSection.Home;

    [ObservableProperty]
    private bool _isNavExpanded = true;

    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new NavItem { Label = "Home",      Icon = "🏠", Section = NavSection.Home },
        new NavItem { Label = "Customers", Icon = "🏢", Section = NavSection.Customers },
        new NavItem { Label = "Settings",  Icon = "⚙",  Section = NavSection.Settings }
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
            NavSection.Customers => AppServiceLocator.Get<CustomersListViewModel>(),
            NavSection.Home      => new HomeViewModel(),
            NavSection.Settings  => new SettingsViewModel(),
            _                    => new HomeViewModel()
        };
    }

    [RelayCommand]
    public void ToggleNav() => IsNavExpanded = !IsNavExpanded;
}
