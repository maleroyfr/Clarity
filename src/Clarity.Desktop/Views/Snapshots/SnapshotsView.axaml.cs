using Avalonia.Controls;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Shell;
using Clarity.Desktop.ViewModels.Snapshots;

namespace Clarity.Desktop.Views.Snapshots;

public partial class SnapshotsView : UserControl
{
    public SnapshotsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SnapshotsViewModel vm)
        {
            _ = vm.LoadCustomersAsync();
            vm.ViewInventoryRequested += OnViewInventory;
            vm.ExportRequested += OnExportRequested;
        }
    }

    private void OnViewInventory(Guid snapshotId)
    {
        if (VisualRoot is Window)
        {
            var shell = AppServiceLocator.Get<AppShellViewModel>();
            shell.NavigateTo(NavSection.Inventory);
        }
    }

    private void OnExportRequested(Guid snapshotId, string snapshotName)
    {
        if (VisualRoot is Window)
        {
            var shell = AppServiceLocator.Get<AppShellViewModel>();
            shell.NavigateToExportSnapshot(snapshotId, snapshotName);
        }
    }
}
