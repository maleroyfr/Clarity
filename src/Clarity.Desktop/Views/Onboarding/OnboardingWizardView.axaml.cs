using Avalonia.Controls;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Onboarding;

namespace Clarity.Desktop.Views.Onboarding;

public partial class OnboardingWizardView : Window
{
    public OnboardingWizardView()
    {
        InitializeComponent();
        var vm = AppServiceLocator.Get<OnboardingWizardViewModel>();
        DataContext = vm;
        vm.Finished += Close;
        vm.Cancelled += Close;
    }
}
