using CommunityToolkit.Mvvm.ComponentModel;

namespace Clarity.Desktop.ViewModels.Shell;

public enum NavSection
{
    Home,
    Customers,
    Environments,
    Snapshots,
    Inventory,
    Comparisons,
    Exports,
    Relations,
    Settings
}

/// <summary>Groups related nav items with an optional header.</summary>
public sealed class NavGroup
{
    public string? Header { get; init; }
    public IReadOnlyList<NavItem> Items { get; init; } = [];
}

public sealed partial class NavItem : ObservableObject
{
    public string Label { get; init; } = default!;
    public string Icon { get; init; } = default!;
    public NavSection Section { get; init; }

    [ObservableProperty]
    private bool _isActive;
}
