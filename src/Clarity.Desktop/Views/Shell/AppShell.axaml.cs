using Avalonia.Controls;
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
}
