using CommunityToolkit.Mvvm.ComponentModel;
using Clarity.SharedContracts.Enums;
using System.Collections.ObjectModel;

namespace Clarity.Desktop.ViewModels.Onboarding;

public sealed class PrerequisiteItemVm
{
    public string Category { get; init; } = default!;
    public string Label { get; init; } = default!;
    public string Description { get; init; } = default!;
    public bool IsOptional { get; init; }
    public bool IsCompleted { get; set; }
}

public sealed partial class PrerequisitesStepViewModel : ObservableObject
{
    public ObservableCollection<PrerequisiteItemVm> Prerequisites { get; } = [];

    public int TotalCount => Prerequisites.Count;
    public int RequiredCount => Prerequisites.Count(p => !p.IsOptional);

    public void BuildFrom(IReadOnlyList<WorkloadArea> selectedAreas)
    {
        Prerequisites.Clear();

        foreach (var area in selectedAreas)
        {
            foreach (var item in GetPrerequisitesFor(area))
                Prerequisites.Add(item);
        }

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RequiredCount));
    }

    private static IEnumerable<PrerequisiteItemVm> GetPrerequisitesFor(WorkloadArea area) =>
        area switch
        {
            WorkloadArea.EntraId => [
                new() { Category = "App Registration", Label = "App Registration in Azure AD", Description = "Register a service principal in Azure AD for Clarity.", IsOptional = false },
                new() { Category = "API Permissions", Label = "User.Read.All, Group.Read.All, RoleManagement.Read.Directory", Description = "Required Microsoft Graph API permissions.", IsOptional = false },
                new() { Category = "Admin Consent", Label = "Admin Consent granted", Description = "Tenant admin must grant consent for the configured permissions.", IsOptional = false },
                new() { Category = "Authentication", Label = "Client certificate (preferred) or client secret", Description = "Certificate-based auth is preferred over client secrets.", IsOptional = false },
            ],
            WorkloadArea.Intune => [
                new() { Category = "API Permissions", Label = "DeviceManagementManagedDevices.Read.All", Description = "Required to read managed device inventory.", IsOptional = false },
                new() { Category = "API Permissions", Label = "DeviceManagementConfiguration.Read.All", Description = "Required to read device configuration profiles.", IsOptional = false },
            ],
            WorkloadArea.ExchangeOnline => [
                new() { Category = "PowerShell Module", Label = "Exchange Online PowerShell module", Description = "ExchangeOnlineManagement module must be installed.", IsOptional = false },
                new() { Category = "Auth Role", Label = "App-only auth role: Exchange.ManageAsApp", Description = "Service principal must have the Exchange.ManageAsApp role.", IsOptional = false },
            ],
            WorkloadArea.SharePointOnline => [
                new() { Category = "API Permissions", Label = "Sites.Read.All", Description = "Required to enumerate SharePoint sites and libraries.", IsOptional = false },
            ],
            WorkloadArea.OnPremAD => [
                new() { Category = "Network", Label = "LDAP network connectivity to domain controllers", Description = "TCP 389 / 636 must be accessible from the collector.", IsOptional = false },
                new() { Category = "Service Account", Label = "Service account with read access to AD", Description = "A dedicated read-only AD account is recommended.", IsOptional = false },
            ],
            _ => []
        };
}
