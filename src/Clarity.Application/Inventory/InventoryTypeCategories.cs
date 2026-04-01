using System.Collections.Frozen;
using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Inventory;

/// <summary>
/// Maps <see cref="InventoryObjectType"/> values to display-friendly category names
/// and human-readable display names.
/// </summary>
public static class InventoryTypeCategories
{
    private static readonly FrozenDictionary<InventoryObjectType, string> CategoryMap =
        new Dictionary<InventoryObjectType, string>
        {
            [InventoryObjectType.EntraUser] = "Entra ID",
            [InventoryObjectType.EntraGroup] = "Entra ID",
            [InventoryObjectType.EntraRole] = "Entra ID",
            [InventoryObjectType.EntraRoleAssignment] = "Entra ID",
            [InventoryObjectType.EntraDevice] = "Entra ID",
            [InventoryObjectType.EntraApplication] = "Entra ID",
            [InventoryObjectType.EntraServicePrincipal] = "Entra ID",
            [InventoryObjectType.EntraConditionalAccessPolicy] = "Entra ID",
            [InventoryObjectType.EntraNamedLocation] = "Entra ID",
            [InventoryObjectType.TenantOrganization] = "Tenant",
            [InventoryObjectType.LicenseSku] = "Licenses",
            [InventoryObjectType.LicenseAssignment] = "Licenses",
            [InventoryObjectType.IntuneDevice] = "Intune",
            [InventoryObjectType.IntuneCompliancePolicy] = "Intune",
            [InventoryObjectType.IntuneConfigurationProfile] = "Intune",
            [InventoryObjectType.IntuneManagedApp] = "Intune",
            [InventoryObjectType.Mailbox] = "Exchange Online",
            [InventoryObjectType.MailUser] = "Exchange Online",
            [InventoryObjectType.DistributionGroup] = "Exchange Online",
            [InventoryObjectType.TransportRule] = "Exchange Online",
            [InventoryObjectType.Connector] = "Exchange Online",
            [InventoryObjectType.SharePointSite] = "SharePoint",
            [InventoryObjectType.SharePointSiteCollection] = "SharePoint",
            [InventoryObjectType.Team] = "Teams",
            [InventoryObjectType.TeamChannel] = "Teams",
            [InventoryObjectType.AdDomain] = "Active Directory",
            [InventoryObjectType.AdOrganizationalUnit] = "Active Directory",
            [InventoryObjectType.AdUser] = "Active Directory",
            [InventoryObjectType.AdGroup] = "Active Directory",
            [InventoryObjectType.AdGroupPolicyObject] = "Active Directory",
            [InventoryObjectType.AdTrust] = "Active Directory",
            [InventoryObjectType.AdSite] = "Active Directory",
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<InventoryObjectType, string> DisplayNameMap =
        new Dictionary<InventoryObjectType, string>
        {
            [InventoryObjectType.EntraUser] = "User",
            [InventoryObjectType.EntraGroup] = "Group",
            [InventoryObjectType.EntraRole] = "Role",
            [InventoryObjectType.EntraRoleAssignment] = "Role Assignment",
            [InventoryObjectType.EntraDevice] = "Device",
            [InventoryObjectType.EntraApplication] = "Application",
            [InventoryObjectType.EntraServicePrincipal] = "Service Principal",
            [InventoryObjectType.EntraConditionalAccessPolicy] = "Conditional Access Policy",
            [InventoryObjectType.EntraNamedLocation] = "Named Location",
            [InventoryObjectType.TenantOrganization] = "Organization",
            [InventoryObjectType.LicenseSku] = "License SKU",
            [InventoryObjectType.LicenseAssignment] = "License Assignment",
            [InventoryObjectType.IntuneDevice] = "Device",
            [InventoryObjectType.IntuneCompliancePolicy] = "Compliance Policy",
            [InventoryObjectType.IntuneConfigurationProfile] = "Configuration Profile",
            [InventoryObjectType.IntuneManagedApp] = "Managed App",
            [InventoryObjectType.Mailbox] = "Mailbox",
            [InventoryObjectType.MailUser] = "Mail User",
            [InventoryObjectType.DistributionGroup] = "Distribution Group",
            [InventoryObjectType.TransportRule] = "Transport Rule",
            [InventoryObjectType.Connector] = "Connector",
            [InventoryObjectType.SharePointSite] = "Site",
            [InventoryObjectType.SharePointSiteCollection] = "Site Collection",
            [InventoryObjectType.Team] = "Team",
            [InventoryObjectType.TeamChannel] = "Channel",
            [InventoryObjectType.AdDomain] = "Domain",
            [InventoryObjectType.AdOrganizationalUnit] = "Organizational Unit",
            [InventoryObjectType.AdUser] = "User",
            [InventoryObjectType.AdGroup] = "Group",
            [InventoryObjectType.AdGroupPolicyObject] = "Group Policy Object",
            [InventoryObjectType.AdTrust] = "Trust",
            [InventoryObjectType.AdSite] = "Site",
        }.ToFrozenDictionary();

    /// <summary>Returns the display-friendly category name for the given object type.</summary>
    public static string GetCategory(InventoryObjectType type) =>
        CategoryMap.TryGetValue(type, out var category) ? category : "Unknown";

    /// <summary>Returns all <see cref="InventoryObjectType"/> values belonging to the specified category.</summary>
    public static IReadOnlyList<InventoryObjectType> GetTypesForCategory(string category) =>
        CategoryMap
            .Where(kv => kv.Value.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>Returns a human-readable display name (e.g. EntraUser → "User").</summary>
    public static string GetDisplayName(InventoryObjectType type) =>
        DisplayNameMap.TryGetValue(type, out var name) ? name : type.ToString();

    /// <summary>Returns all distinct category names.</summary>
    public static IReadOnlyList<string> GetAllCategories() =>
        CategoryMap.Values.Distinct().ToList();
}
