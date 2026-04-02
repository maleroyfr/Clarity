using CommunityToolkit.Mvvm.ComponentModel;

namespace Clarity.Desktop.ViewModels.Shell;

public enum NavSection
{
    Home,
    Customers,
    Environments,
    Inventory,
    Settings
}

public sealed partial class NavItem : ObservableObject
{
    public string Label { get; init; } = default!;
    public string Icon { get; init; } = default!;
    public NavSection Section { get; init; }

    [ObservableProperty]
    private bool _isActive;
}
