using Avalonia.Controls;
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
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel vm)
            vm.ShowAboutDialog();
    }
}
