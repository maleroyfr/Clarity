using Avalonia.Controls;
using Clarity.Desktop.ViewModels.Shell;

namespace Clarity.Desktop.Views.Shell;

public partial class AboutView : Window
{
    public AboutView()
    {
        InitializeComponent();
        var vm = new AboutViewModel();
        DataContext = vm;
        vm.CloseRequested += Close;
    }
}
