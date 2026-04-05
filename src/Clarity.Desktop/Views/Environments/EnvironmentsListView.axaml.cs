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

        if (VisualRoot is not SukiWindow window) return;
        if (window.DataContext is not Clarity.Desktop.ViewModels.Shell.AppShellViewModel shell) return;

        var wizardVm = AppServiceLocator.Get<EnvironmentSetupWizardViewModel>();
        wizardVm.Initialize(environment, listVm.SelectedCustomer.Id);

        var wizardView = new EnvironmentSetupWizardView { DataContext = wizardVm };

        var builder = new SukiDialogBuilder(shell.DialogManager);
        builder.SetTitle(wizardVm.WizardTitle);
        builder.SetContent(wizardView);
        builder.Dismiss().ByClickingBackground();

        wizardVm.Completed += async () =>
        {
            try
            {
                shell.DialogManager.DismissDialog();
                if (DataContext is EnvironmentsListViewModel lvm)
                    await lvm.LoadAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to reload environments: {ex.Message}");
            }
        };

        wizardVm.Cancelled += () =>
        {
            try { shell.DialogManager.DismissDialog(); }
            catch { /* dismissal is best-effort */ }
        };

        builder.TryShow();
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
