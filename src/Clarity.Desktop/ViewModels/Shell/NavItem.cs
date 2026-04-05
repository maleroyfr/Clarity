using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

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

/// <summary>
/// Page model for SukiSideMenu items. Each instance drives one menu item.
/// </summary>
public partial class NavPageItem : ObservableObject
{
    public string Title { get; }
    public string IconData { get; }
    public NavSection Section { get; }
    public ObservableCollection<NavPageItem>? Children { get; }

    [ObservableProperty]
    private ObservableObject? _pageVm;

    public NavPageItem(string title, string iconData, NavSection section, ObservableObject? pageVm = null)
    {
        Title = title;
        IconData = iconData;
        Section = section;
        PageVm = pageVm;
    }
}
