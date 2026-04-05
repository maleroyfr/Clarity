using Avalonia.Controls;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Customers;

namespace Clarity.Desktop.Views.Customers;

public partial class CustomerFormView : UserControl
{
    public CustomerFormView()
    {
        InitializeComponent();
        var vm = AppServiceLocator.Get<CustomerFormViewModel>();
        DataContext = vm;
    }
}
