using FluentAvalonia.UI.Controls;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Customers;

namespace Clarity.Desktop.Views.Customers;

public partial class CustomerFormView : ContentDialog
{
    public CustomerFormView()
    {
        InitializeComponent();
        var vm = AppServiceLocator.Get<CustomerFormViewModel>();
        DataContext = vm;
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        var vm = (CustomerFormViewModel)DataContext!;
        try
        {
            await vm.SaveCommand.ExecuteAsync(null);
            // Keep dialog open if save had an error
            if (vm.ErrorMessage is not null)
                args.Cancel = true;
        }
        catch
        {
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
