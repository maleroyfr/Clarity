using Avalonia.Controls;
using Clarity.Desktop.ViewModels.Relations;

namespace Clarity.Desktop.Views.Relations;

public partial class RelationsView : UserControl
{
    public RelationsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is RelationsViewModel vm)
        {
            _ = vm.LoadCustomersAsync();
        }
    }
}
