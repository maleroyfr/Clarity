using Avalonia.Controls;
using Clarity.Application.Customers.Commands;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Customers;
using SukiUI.Controls;
using SukiUI.Dialogs;

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

    private void OnEditRequested(CustomerDto? customer)
    {
        var form = new CustomerFormView();
        var formVm = (CustomerFormViewModel)form.DataContext!;
        formVm.Initialize(customer);

        if (VisualRoot is SukiWindow window && window.DataContext is Clarity.Desktop.ViewModels.Shell.AppShellViewModel shell)
        {
            formVm.SaveCompleted += async () =>
            {
                if (DataContext is CustomersListViewModel listVm)
                    await listVm.LoadAsync();
            };

            var builder = new SukiDialogBuilder(shell.DialogManager);
            builder.SetTitle(formVm.Title);
            builder.SetContent(form);
            builder.AddActionButton("Save", async _ =>
            {
                await formVm.SaveCommand.ExecuteAsync(null);
            }, true);
            builder.AddActionButton("Cancel", _ => { });
            builder.TryShow();
        }
    }

    private void OnViewEnvironmentsRequested(Guid customerId)
    {
        if (VisualRoot is SukiWindow window && window.DataContext is Clarity.Desktop.ViewModels.Shell.AppShellViewModel shell)
        {
            shell.NavigateToCustomerEnvironments(customerId);
        }
    }
}
