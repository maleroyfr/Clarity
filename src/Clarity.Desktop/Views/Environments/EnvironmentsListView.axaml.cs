using Avalonia.Controls;
using Clarity.Application.Environments;
using Clarity.Application.Environments.Queries;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Environments;
using SukiUI.Controls;
using SukiUI.Dialogs;
using MediatR;

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
            vm.ConfigureAuthRequested += OnConfigureAuthRequested;
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
            try
            {
                if (DataContext is EnvironmentsListViewModel lvm)
                    await lvm.LoadAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to reload environments: {ex.Message}");
            }
        };
        form.ShowDialog(VisualRoot as Avalonia.Controls.Window
            ?? throw new InvalidOperationException());
    }

    private async void OnConfigureAuthRequested(EnvironmentDto environment)
    {
        try
        {
            var mediator = AppServiceLocator.Get<IMediator>();
            var detail = await mediator.Send(new GetEnvironmentDetailQuery(environment.Id));

            var authVm = AppServiceLocator.Get<AuthConfigViewModel>();
            authVm.Initialize(detail);

            var authView = new AuthConfigView { DataContext = authVm };

            if (VisualRoot is SukiWindow window && window.DataContext is Clarity.Desktop.ViewModels.Shell.AppShellViewModel shell)
            {
                var builder = new SukiDialogBuilder(shell.DialogManager);
                builder.SetTitle("Authentication Configuration");
                builder.SetContent(authView);
                builder.AddActionButton("Close", async _ =>
                {
                    try
                    {
                        if (DataContext is EnvironmentsListViewModel lvm)
                            await lvm.LoadAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to reload environments: {ex.Message}");
                    }
                }, true, ["Flat"]);
                builder.TryShow();
            }
        }
        catch (Exception ex)
        {
            if (DataContext is EnvironmentsListViewModel lvm)
                lvm.ErrorMessage = $"Failed to open auth config: {ex.Message}";
        }
    }
}
