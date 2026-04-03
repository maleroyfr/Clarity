using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Clarity.Desktop.ViewModels.Exports;

namespace Clarity.Desktop.Views.Exports;

public partial class ExportsView : UserControl
{
    public ExportsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ExportsViewModel vm)
        {
            vm.BrowseRequested += OnBrowseRequested;
            _ = vm.LoadCustomersAsync();
        }
    }

    private async void OnBrowseRequested()
    {
        if (DataContext is not ExportsViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose export location",
            SuggestedFileName = "export",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV files") { Patterns = ["*.csv"] },
                new FilePickerFileType("Excel files") { Patterns = ["*.xlsx"] },
                new FilePickerFileType("JSON files") { Patterns = ["*.json"] },
                new FilePickerFileType("All files") { Patterns = ["*.*"] }
            ]
        });

        if (file is not null)
        {
            vm.OutputPath = file.Path.LocalPath;
        }
    }
}
