using Avalonia.Controls;
using Clarity.Desktop.ViewModels.Comparisons;

namespace Clarity.Desktop.Views.Comparisons;

public partial class ComparisonView : UserControl
{
    public ComparisonView() { InitializeComponent(); }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ComparisonViewModel vm)
            _ = vm.LoadCustomersAsync();
    }
}
