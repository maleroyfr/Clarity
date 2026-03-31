namespace Clarity.Desktop.ViewModels.Shell;

public enum NavSection
{
    Home,
    Customers,
    Settings
}

public sealed class NavItem
{
    public string Label { get; init; } = default!;
    public string Icon { get; init; } = default!;
    public NavSection Section { get; init; }
}
