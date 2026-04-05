using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.Desktop.Services;
using Clarity.Desktop.ViewModels.Comparisons;
using Clarity.Desktop.ViewModels.Customers;
using Clarity.Desktop.ViewModels.Environments;
using Clarity.Desktop.ViewModels.Exports;
using Clarity.Desktop.ViewModels.Inventory;
using Clarity.Desktop.ViewModels.Relations;
using Clarity.Desktop.ViewModels.Snapshots;
using Clarity.Desktop.ViewModels.Onboarding;
using Avalonia.Controls.Notifications;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using SukiUI.Enums;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Shell;

public sealed partial class AppShellViewModel : ObservableObject
{
    // SukiUI dialog & toast managers (used by views via binding)
    public ISukiDialogManager DialogManager { get; }
    public ISukiToastManager ToastManager { get; }

    // Side menu page collection
    public ObservableCollection<NavPageItem> Pages { get; }

    [ObservableProperty]
    private NavPageItem? _activePage;

    // Settings lives in footer, tracked separately
    public SettingsViewModel SettingsPage { get; }

    public AppShellViewModel()
    {
        DialogManager = new SukiDialogManager();
        ToastManager = new SukiToastManager();
        SettingsPage = AppServiceLocator.Get<SettingsViewModel>();

        Pages = new ObservableCollection<NavPageItem>
        {
            MakePage("Home",         Icons.Home,         NavSection.Home),
            MakePage("Customers",    Icons.People,       NavSection.Customers),
            MakePage("Environments", Icons.Globe,        NavSection.Environments),
            MakePage("Relations",    Icons.Link,         NavSection.Relations),
            MakePage("Snapshots",    Icons.Camera,       NavSection.Snapshots),
            MakePage("Inventory",    Icons.AllApps,      NavSection.Inventory),
            MakePage("Comparisons",  Icons.Library,      NavSection.Comparisons),
            MakePage("Exports",      Icons.Download,     NavSection.Exports),
            MakePage("Onboarding",   Icons.Wizard,       NavSection.Onboarding),
        };

        ActivePage = Pages[0]; // Home
    }

    /// <summary>Shows a simple About dialog.</summary>
    public void ShowAboutDialog()
    {
        var builder = new SukiDialogBuilder(DialogManager);
        builder.SetTitle("About Clarity");
        builder.SetContent("Clarity — Audit & Discovery Platform\nVersion 1.0.0\n\n© 2025 Clarity Team");
        builder.AddActionButton("Close", _ => { }, true, ["Flat"]);
        builder.TryShow();
    }

    /// <summary>Shows a toast notification.</summary>
    public void ShowToast(string title, string message, NotificationType type = NotificationType.Information)
    {
        var builder = new SukiToastBuilder(ToastManager);
        builder.SetTitle(title);
        builder.SetContent(message);
        builder.SetType(type);
        builder.SetDismissAfter(TimeSpan.FromSeconds(4));
        builder.Queue();
    }

    // ─── Cross-page navigation helpers ──────────────────────────────

    public void NavigateToCustomerEnvironments(Guid customerId)
    {
        NavigateToSection(NavSection.Environments);
        if (ActivePage?.PageVm is EnvironmentsListViewModel vm)
            _ = LoadAndSelectCustomerAsync(vm, customerId);
    }

    public void NavigateToEnvironmentSnapshots(Guid customerId, Guid environmentId, string environmentName)
    {
        NavigateToSection(NavSection.Snapshots);
        if (ActivePage?.PageVm is SnapshotsViewModel vm)
            _ = vm.LoadForEnvironmentAsync(customerId, environmentId, environmentName);
    }

    public void NavigateToExportSnapshot(Guid snapshotId, string snapshotName)
    {
        NavigateToSection(NavSection.Exports);
        if (ActivePage?.PageVm is ExportsViewModel vm)
            vm.SetSnapshotContext(snapshotId, snapshotName);
    }

    /// <summary>Navigate to a section by enum value.</summary>
    [RelayCommand]
    public void NavigateToSection(NavSection section)
    {
        var page = Pages.FirstOrDefault(p => p.Section == section);
        if (page is not null)
            ActivePage = page;
    }

    // ─── Private helpers ────────────────────────────────────────────

    private static NavPageItem MakePage(string title, string iconData, NavSection section)
    {
        var vm = ResolveVm(section);
        return new NavPageItem(title, iconData, section, vm);
    }

    private static ObservableObject? ResolveVm(NavSection section) => section switch
    {
        NavSection.Home         => AppServiceLocator.Get<HomeViewModel>(),
        NavSection.Customers    => AppServiceLocator.Get<CustomersListViewModel>(),
        NavSection.Environments => AppServiceLocator.Get<EnvironmentsListViewModel>(),
        NavSection.Relations    => AppServiceLocator.Get<RelationsViewModel>(),
        NavSection.Snapshots    => AppServiceLocator.Get<SnapshotsViewModel>(),
        NavSection.Inventory    => AppServiceLocator.Get<InventoryExplorerViewModel>(),
        NavSection.Comparisons  => AppServiceLocator.Get<ComparisonViewModel>(),
        NavSection.Exports      => AppServiceLocator.Get<ExportsViewModel>(),
        NavSection.Onboarding   => AppServiceLocator.Get<OnboardingWizardViewModel>(),
        _                       => null
    };

    private static async Task LoadAndSelectCustomerAsync(EnvironmentsListViewModel vm, Guid customerId)
    {
        await vm.LoadCustomersAsync();
        vm.SetCustomer(customerId);
    }

    // ─── Material Design icon path data ─────────────────────────────
    private static class Icons
    {
        public const string Home = "M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z";
        public const string People = "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z";
        public const string Globe = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z";
        public const string Link = "M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z";
        public const string Camera = "M9 2L7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z";
        public const string AllApps = "M4 8h4V4H4v4zm6 12h4v-4h-4v4zm-6 0h4v-4H4v4zm0-6h4v-4H4v4zm6 0h4v-4h-4v4zm6-10v4h4V4h-4zm-6 4h4V4h-4v4zm6 6h4v-4h-4v4zm0 6h4v-4h-4v4z";
        public const string Library = "M4 6H2v14c0 1.1.9 2 2 2h14v-2H4V6zm16-4H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-1 9H9V9h10v2zm-4 4H9v-2h6v2zm4-8H9V5h10v2z";
        public const string Download = "M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z";
        public const string Wizard = "M12 3L1 9l4 2.18v6L12 21l7-3.82v-6l2-1.09V17h2V9L12 3zm6.82 6L12 12.72 5.18 9 12 5.28 18.82 9zM17 15.99l-5 2.73-5-2.73v-3.72L12 15l5-2.73v3.72z";
    }
}
