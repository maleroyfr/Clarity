using CommunityToolkit.Mvvm.ComponentModel;

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class HomeViewModel : ObservableObject
{
    public string WelcomeMessage { get; } = "Welcome to Clarity";
    public string SubMessage { get; } = "Select a customer to get started, or create a new one.";
}

public sealed partial class SettingsViewModel : ObservableObject
{
    public string Title { get; } = "Settings";
}
