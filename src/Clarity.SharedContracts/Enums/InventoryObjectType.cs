namespace Clarity.SharedContracts.Enums;

public enum InventoryObjectType
{
    // Entra ID
    EntraUser,
    EntraGroup,
    EntraRole,
    EntraRoleAssignment,
    EntraDevice,
    EntraApplication,
    EntraServicePrincipal,
    EntraConditionalAccessPolicy,
    EntraNamedLocation,

    // Licenses
    LicenseSku,
    LicenseAssignment,

    // Intune
    IntuneDevice,
    IntuneCompliancePolicy,
    IntuneConfigurationProfile,

    // Exchange Online
    Mailbox,
    MailUser,
    DistributionGroup,
    TransportRule,
    Connector,

    // SharePoint
    SharePointSite,
    SharePointSiteCollection,

    // Teams
    Team,
    TeamChannel,

    // On-premises Active Directory
    AdDomain,
    AdOrganizationalUnit,
    AdUser,
    AdGroup,
    AdGroupPolicyObject,
    AdTrust,
    AdSite
}
