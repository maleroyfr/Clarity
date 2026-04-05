using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Clarity.Desktop.ViewModels.Environments;

namespace Clarity.Desktop.Views.Environments;

public partial class EnvironmentSetupWizardView : UserControl
{
    public EnvironmentSetupWizardView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is EnvironmentSetupWizardViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(EnvironmentSetupWizardViewModel.CurrentStep))
                    RebuildStepIndicator(vm.CurrentStep);
            };
            RebuildStepIndicator(vm.CurrentStep);
        }
    }

    private void RebuildStepIndicator(int currentStep)
    {
        var panel = this.FindControl<StackPanel>("StepIndicatorPanel");
        if (panel is null) return;

        panel.Children.Clear();

        for (int i = 0; i < EnvironmentSetupWizardViewModel.StepTitles.Length; i++)
        {
            var stepTitle = EnvironmentSetupWizardViewModel.StepTitles[i];
            var isDone = i < currentStep;
            var isActive = i == currentStep;

            // Step circle with number or checkmark
            var circleText = new TextBlock
            {
                Text = isDone ? "✓" : (i + 1).ToString(),
                FontSize = isDone ? 16 : 13,
                FontWeight = FontWeight.Bold,
                Foreground = (isDone || isActive) ? Brushes.White : new SolidColorBrush(Color.Parse("#808080")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            IBrush circleBg;
            if (isDone || isActive)
            {
                // Use the theme primary color dynamically
                if (this.TryFindResource("SukiPrimaryColor", this.ActualThemeVariant, out var res) && res is IBrush primary)
                    circleBg = primary;
                else
                    circleBg = new SolidColorBrush(Color.Parse("#3498DB"));
            }
            else
            {
                circleBg = new SolidColorBrush(Color.Parse("#30808080"));
            }

            var circle = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = circleBg,
                Child = circleText
            };

            // Step label
            var label = new TextBlock
            {
                Text = stepTitle,
                FontSize = 12,
                FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Normal,
                Opacity = (isDone || isActive) ? 1.0 : 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var stepStack = new StackPanel
            {
                Spacing = 0,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 80
            };
            stepStack.Children.Add(new Avalonia.Controls.Decorator
            {
                Child = circle,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stepStack.Children.Add(label);

            panel.Children.Add(stepStack);

            // Add connector line between steps
            if (i < EnvironmentSetupWizardViewModel.StepTitles.Length - 1)
            {
                var connectorBg = i < currentStep
                    ? circleBg
                    : new SolidColorBrush(Color.Parse("#30808080"));

                var connector = new Border
                {
                    Height = 2,
                    Width = 40,
                    Background = connectorBg,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 18) // offset to align with circles
                };

                panel.Children.Add(connector);
            }
        }
    }
}
