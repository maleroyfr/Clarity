using Avalonia.Controls;
using Clarity.Application.Customers.Commands;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Customers;
using Clarity.Desktop.ViewModels.Shell;
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

            // Wire global keyboard shortcuts
            var shell = AppServiceLocator.Get<AppShellViewModel>();
            shell.CreateNewRequested += () =>
            {
                if (shell.ActivePage?.Section == NavSection.Customers)
                    vm.CreateNewCommand.Execute(null);
            };
            shell.RefreshRequested += () =>
            {
                if (shell.ActivePage?.Section == NavSection.Customers)
                    _ = vm.LoadAsync();
            };

            _ = vm.LoadAsync();
        }
    }

    private void OnEditRequested(CustomerDto? customer)
    {
        var form = new CustomerFormView();
        var formVm = (CustomerFormViewModel)form.DataContext!;
        formVm.Initialize(customer);

        if (VisualRoot is SukiWindow window && window.DataContext is AppShellViewModel shell)
        {
            formVm.SaveCompleted += async () =>
            {
                try
                {
                    if (DataContext is CustomersListViewModel listVm)
                        await listVm.LoadAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to reload customers: {ex.Message}");
                }
            };

            var builder = new SukiDialogBuilder(shell.DialogManager);
            builder.SetTitle(formVm.Title);
            builder.SetContent(form);
            builder.AddActionButton("Save", async _ =>
            {
                try
                {
                    await formVm.SaveCommand.ExecuteAsync(null);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Save failed: {ex.Message}");
                }
            }, true, ["Flat"]);
            builder.AddActionButton("Cancel", _ => { }, true, ["Flat"]);
            builder.TryShow();
        }
    }

    private void OnViewEnvironmentsRequested(Guid customerId)
    {
        if (VisualRoot is SukiWindow window && window.DataContext is AppShellViewModel shell)
        {
            shell.NavigateToCustomerEnvironments(customerId);
        }
    }
}
