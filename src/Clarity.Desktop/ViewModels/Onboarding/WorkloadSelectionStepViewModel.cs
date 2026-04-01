using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Clarity.SharedContracts.Enums;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Onboarding;

public sealed partial class WorkloadItemVm : ObservableObject
{
    public WorkloadArea WorkloadArea { get; }
    public string Label { get; }
    public string Description { get; }

    [ObservableProperty]
    private bool _isSelected;

    public WorkloadItemVm(WorkloadArea area, string label, string description)
    {
        WorkloadArea = area;
        Label = label;
        Description = description;
    }
}

public sealed partial class WorkloadSelectionStepViewModel : ObservableObject
{
    public ObservableCollection<WorkloadItemVm> Workloads { get; } =
    [
        new(WorkloadArea.EntraId,          "Entra ID",           "Azure AD users, groups, roles and conditional access"),
        new(WorkloadArea.Intune,           "Intune",             "Device management, compliance policies and configuration profiles"),
        new(WorkloadArea.ExchangeOnline,   "Exchange Online",    "Mailboxes, distribution groups and mail-flow rules"),
        new(WorkloadArea.SharePointOnline, "SharePoint Online",  "Sites, libraries, permissions and sharing settings"),
        new(WorkloadArea.Teams,            "Microsoft Teams",    "Teams, channels, policies and calling configuration"),
        new(WorkloadArea.OnPremAD,         "On-Premises AD",     "Active Directory users, OUs, GPOs and trusts"),
        new(WorkloadArea.OnPremExchange,   "On-Premises Exchange", "On-prem mailboxes, connectors and transport rules"),
    ];

    public bool HasSelection => Workloads.Any(w => w.IsSelected);

    public IReadOnlyList<WorkloadArea> SelectedAreas =>
        Workloads.Where(w => w.IsSelected).Select(w => w.WorkloadArea).ToList();
}
