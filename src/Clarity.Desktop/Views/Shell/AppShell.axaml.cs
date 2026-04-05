using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SukiUI.Controls;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;

namespace Clarity.Desktop.Views.Shell;

public partial class AppShell : SukiWindow
{
    public AppShell()
    {
        InitializeComponent();
        DataContext = AppServiceLocator.Get<AppShellViewModel>();
        KeyDown += OnGlobalKeyDown;
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not AppShellViewModel vm) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl)
        {
            NavSection? section = e.Key switch
            {
                Key.D1 => NavSection.Home,
                Key.D2 => NavSection.Customers,
                Key.D3 => NavSection.Environments,
                Key.D4 => NavSection.Relations,
                Key.D5 => NavSection.Snapshots,
                Key.D6 => NavSection.Inventory,
                Key.D7 => NavSection.Comparisons,
                Key.D8 => NavSection.Exports,
                Key.OemComma => NavSection.Settings,
                _ => null
            };

            if (section is not null)
            {
                vm.NavigateToSection(section.Value);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.N)
            {
                vm.RequestCreateNew();
                e.Handled = true;
                return;
            }

            if (e.Key is Key.B)
            {
                SideMenu.IsMenuExpanded = !SideMenu.IsMenuExpanded;
                e.Handled = true;
                return;
            }
        }

        if (e.Key is Key.F5)
        {
            vm.RequestRefresh();
            e.Handled = true;
        }
        else if (e.Key is Key.F1)
        {
            vm.ShowAboutDialog();
            e.Handled = true;
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel vm)
            vm.RequestCreateNew();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel vm)
            vm.RequestRefresh();
    }

    private void OnToggleSidebarClick(object? sender, RoutedEventArgs e)
    {
        SideMenu.IsMenuExpanded = !SideMenu.IsMenuExpanded;
    }

    private void OnNavigateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && DataContext is AppShellViewModel vm)
        {
            if (Enum.TryParse<NavSection>(tag, out var section))
                vm.NavigateToSection(section);
        }
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel vm)
            vm.ShowAboutDialog();
    }
}
