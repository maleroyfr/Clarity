using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Mappings;

/// <summary>
/// Maps InventoryObjectType values to their parent WorkloadArea for grouping and filtering.
/// </summary>
public static class WorkloadAreaMapping
{
    private static readonly Dictionary<InventoryObjectType, WorkloadArea> Map = new()
    {
        // Entra ID
        { InventoryObjectType.EntraUser,                    WorkloadArea.EntraId },
        { InventoryObjectType.EntraGroup,                   WorkloadArea.EntraId },
        { InventoryObjectType.EntraRole,                    WorkloadArea.EntraId },
        { InventoryObjectType.EntraRoleAssignment,          WorkloadArea.EntraId },
        { InventoryObjectType.EntraDevice,                  WorkloadArea.EntraId },
        { InventoryObjectType.EntraApplication,             WorkloadArea.EntraId },
        { InventoryObjectType.EntraServicePrincipal,        WorkloadArea.EntraId },
        { InventoryObjectType.EntraConditionalAccessPolicy, WorkloadArea.EntraId },
        { InventoryObjectType.EntraNamedLocation,           WorkloadArea.EntraId },
        { InventoryObjectType.TenantOrganization,          WorkloadArea.EntraId },
        { InventoryObjectType.LicenseSku,                   WorkloadArea.EntraId },
        { InventoryObjectType.LicenseAssignment,            WorkloadArea.EntraId },

        // Intune
        { InventoryObjectType.IntuneDevice,               WorkloadArea.Intune },
        { InventoryObjectType.IntuneCompliancePolicy,     WorkloadArea.Intune },
        { InventoryObjectType.IntuneConfigurationProfile, WorkloadArea.Intune },
        { InventoryObjectType.IntuneManagedApp,          WorkloadArea.Intune },

        // Exchange Online
        { InventoryObjectType.Mailbox,           WorkloadArea.ExchangeOnline },
        { InventoryObjectType.MailUser,          WorkloadArea.ExchangeOnline },
        { InventoryObjectType.DistributionGroup, WorkloadArea.ExchangeOnline },
        { InventoryObjectType.TransportRule,     WorkloadArea.ExchangeOnline },
        { InventoryObjectType.Connector,         WorkloadArea.ExchangeOnline },

        // SharePoint Online
        { InventoryObjectType.SharePointSite,           WorkloadArea.SharePointOnline },
        { InventoryObjectType.SharePointSiteCollection, WorkloadArea.SharePointOnline },

        // Teams
        { InventoryObjectType.Team,        WorkloadArea.Teams },
        { InventoryObjectType.TeamChannel, WorkloadArea.Teams },

        // On-premises AD
        { InventoryObjectType.AdDomain,             WorkloadArea.OnPremAD },
        { InventoryObjectType.AdOrganizationalUnit, WorkloadArea.OnPremAD },
        { InventoryObjectType.AdUser,               WorkloadArea.OnPremAD },
        { InventoryObjectType.AdGroup,              WorkloadArea.OnPremAD },
        { InventoryObjectType.AdGroupPolicyObject,  WorkloadArea.OnPremAD },
        { InventoryObjectType.AdTrust,              WorkloadArea.OnPremAD },
        { InventoryObjectType.AdSite,               WorkloadArea.OnPremAD },
    };

    public static WorkloadArea GetWorkloadArea(InventoryObjectType type) =>
        Map.TryGetValue(type, out var area) ? area : WorkloadArea.EntraId;

    public static IEnumerable<InventoryObjectType> GetObjectTypes(WorkloadArea area) =>
        Map.Where(kv => kv.Value == area).Select(kv => kv.Key);
}
