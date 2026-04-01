using Avalonia.Controls;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Customers;

namespace Clarity.Desktop.Views.Customers;

public partial class CustomerFormView : Window
{
    public CustomerFormView()
    {
        InitializeComponent();
        var vm = AppServiceLocator.Get<CustomerFormViewModel>();
        DataContext = vm;
        vm.SaveCompleted += Close;
        vm.Cancelled += Close;
    }
}
