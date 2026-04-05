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

public sealed class WorkloadGroupVm
{
    public string GroupName { get; }
    public string GroupIcon { get; }
    public ObservableCollection<WorkloadItemVm> Workloads { get; }

    public WorkloadGroupVm(string name, string icon, ObservableCollection<WorkloadItemVm> workloads)
    {
        GroupName = name;
        GroupIcon = icon;
        Workloads = workloads;
    }
}

public sealed partial class WorkloadSelectionStepViewModel : ObservableObject
{
    public ObservableCollection<WorkloadGroupVm> WorkloadGroups { get; } =
    [
        new("Microsoft 365 Cloud", "☁️",
        [
            new(WorkloadArea.EntraId,          "Microsoft Entra ID",         "Users, groups, roles, applications, devices, and conditional access policies"),
            new(WorkloadArea.Intune,           "Microsoft Intune",           "Device management, compliance policies, configuration profiles, and app inventory"),
            new(WorkloadArea.ExchangeOnline,   "Exchange Online",            "Mailboxes, distribution groups, mail-flow rules, and transport settings"),
            new(WorkloadArea.SharePointOnline, "SharePoint Online",          "Sites, libraries, permissions, sharing settings, and storage quotas"),
            new(WorkloadArea.Teams,            "Microsoft Teams",            "Teams, channels, policies, calling configuration, and meeting settings"),
        ]),
        new("On-Premises Infrastructure", "🏢",
        [
            new(WorkloadArea.OnPremAD,         "Active Directory",           "AD users, groups, OUs, GPOs, trusts, and domain controllers"),
            new(WorkloadArea.OnPremExchange,   "Exchange Server",            "On-premises mailboxes, connectors, transport rules, and recipient configuration"),
        ])
    ];

    public ObservableCollection<WorkloadItemVm> Workloads { get; } = [];

    public WorkloadSelectionStepViewModel()
    {
        foreach (var group in WorkloadGroups)
            foreach (var item in group.Workloads)
                Workloads.Add(item);
    }

    public bool HasSelection => Workloads.Any(w => w.IsSelected);

    public IReadOnlyList<WorkloadArea> SelectedAreas =>
        Workloads.Where(w => w.IsSelected).Select(w => w.WorkloadArea).ToList();
}
