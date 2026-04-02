using Avalonia.Controls;
using Avalonia.Interactivity;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;

namespace Clarity.Desktop.Views.Shell;

public partial class AppShell : Window
{
    public AppShell()
    {
        InitializeComponent();
        DataContext = AppServiceLocator.Get<AppShellViewModel>();

        var aboutBtn = this.FindControl<Button>("AboutButton");
        if (aboutBtn is not null)
            aboutBtn.Click += OnAboutClick;
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var about = new AboutView();
        about.ShowDialog(this);
    }
}
