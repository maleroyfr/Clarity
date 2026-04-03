using Avalonia.Controls;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;

namespace Clarity.Desktop.Views.Shell;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is HomeViewModel vm)
        {
            vm.NavigateRequested += OnNavigateRequested;
            _ = vm.LoadAsync();
        }
    }

    private void OnNavigateRequested(NavSection section)
    {
        var shell = AppServiceLocator.Get<AppShellViewModel>();
        shell.NavigateTo(section);
    }
}
