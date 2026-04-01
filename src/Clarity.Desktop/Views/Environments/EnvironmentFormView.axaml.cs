using Avalonia.Controls;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Environments;

namespace Clarity.Desktop.Views.Environments;

public partial class EnvironmentFormView : Window
{
    public EnvironmentFormView()
    {
        InitializeComponent();
        var vm = AppServiceLocator.Get<EnvironmentFormViewModel>();
        DataContext = vm;
        vm.SaveCompleted += Close;
        vm.Cancelled += Close;
    }
}
