using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;

namespace Clarity.Desktop.Views.Shell;

public partial class AppShell : Window
{
    public AppShell()
    {
        InitializeComponent();
        DataContext = AppServiceLocator.Get<AppShellViewModel>();
    }

    private void NavView_SelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (DataContext is not AppShellViewModel vm) return;

        if (e.IsSettingsSelected)
        {
            vm.NavigateTo(NavSection.Settings);
            return;
        }

        if (e.SelectedItem is NavigationViewItem nvi && nvi.Tag is string tag)
        {
            if (Enum.TryParse<NavSection>(tag, out var section))
                vm.NavigateTo(section);
        }
    }
}
