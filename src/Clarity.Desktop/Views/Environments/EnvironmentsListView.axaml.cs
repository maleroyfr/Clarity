using Avalonia.Controls;
using Clarity.Application.Environments;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Environments;

namespace Clarity.Desktop.Views.Environments;

public partial class EnvironmentsListView : UserControl
{
    public EnvironmentsListView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EnvironmentsListViewModel vm)
        {
            vm.EditRequested += OnEditRequested;
            _ = vm.LoadCustomersAsync();
        }
    }

    private void OnEditRequested(EnvironmentDto? environment)
    {
        if (DataContext is not EnvironmentsListViewModel listVm) return;
        if (listVm.SelectedCustomer is null) return;

        var form = new EnvironmentFormView();
        var formVm = (EnvironmentFormViewModel)form.DataContext!;
        formVm.Initialize(environment, customerId: listVm.SelectedCustomer.Id);
        formVm.SaveCompleted += async () =>
        {
            if (DataContext is EnvironmentsListViewModel lvm)
                await lvm.LoadAsync();
        };
        form.ShowDialog(VisualRoot as Avalonia.Controls.Window
            ?? throw new InvalidOperationException());
    }
}
