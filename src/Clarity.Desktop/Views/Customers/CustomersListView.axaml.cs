using Avalonia.Controls;
using Clarity.Application.Customers.Commands;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Customers;
using FluentAvalonia.UI.Controls;

namespace Clarity.Desktop.Views.Customers;

public partial class CustomersListView : UserControl
{
    public CustomersListView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is CustomersListViewModel vm)
        {
            vm.EditRequested += OnEditRequested;
            vm.ViewEnvironmentsRequested += OnViewEnvironmentsRequested;
            _ = vm.LoadAsync();
        }
    }

    private async void OnEditRequested(CustomerDto? customer)
    {
        var form = new CustomerFormView();
        var formVm = (CustomerFormViewModel)form.DataContext!;
        formVm.Initialize(customer);

        var result = await form.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (DataContext is CustomersListViewModel listVm)
                await listVm.LoadAsync();
        }
    }

    private void OnViewEnvironmentsRequested(Guid customerId)
    {
        if (VisualRoot is Window window && window.DataContext is Clarity.Desktop.ViewModels.Shell.AppShellViewModel shell)
        {
            shell.NavigateToCustomerEnvironments(customerId);
        }
    }
}
