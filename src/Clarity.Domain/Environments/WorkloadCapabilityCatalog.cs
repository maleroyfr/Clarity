using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Environments;

public sealed record GraphPermissionRequirement(
    string Name,
    string Description,
    bool IsOptional = false);

public sealed record PowerShellModuleRequirement(
    string ModuleName,
    Version MinimumVersion,
    string Purpose);

public sealed record WorkloadChecklistItemDefinition(
    string Key,
    string Label,
    string Description,
    PrerequisiteCategory Category,
    bool IsRequired,
    bool IsAutoDetectable = false,
    bool IsAutoFixSupported = false);

public sealed record WorkloadCapabilityDefinition(
    WorkloadArea Area,
    string DisplayName,
    string Description,
    IReadOnlyList<GraphPermissionRequirement> RequiredPermissions,
    IReadOnlyList<GraphPermissionRequirement> OptionalPermissions,
    IReadOnlyList<PowerShellModuleRequirement> RequiredPowerShellModules,
    IReadOnlyList<WorkloadChecklistItemDefinition> AdditionalPrerequisites);

public static class WorkloadCapabilityCatalog
{
    private static readonly IReadOnlyDictionary<WorkloadArea, WorkloadCapabilityDefinition> Definitions =
        new Dictionary<WorkloadArea, WorkloadCapabilityDefinition>
        {
            [WorkloadArea.EntraId] = new(
                WorkloadArea.EntraId,
                "Entra ID",
                "Users, groups, roles, applications, devices, and conditional access.",
                RequiredPermissions:
                [
                    new("Organization.Read.All", "Read tenant organization profile."),
                    new("Domain.Read.All", "Read verified and federated domains."),
                    new("User.Read.All", "Read directory users."),
                    new("Group.Read.All", "Read groups and memberships."),
                    new("Application.Read.All", "Read app registrations and enterprise apps."),
                    new("Device.Read.All", "Read device identities."),
                    new("RoleManagement.Read.Directory", "Read directory roles and assignments."),
                    new("UserAuthenticationMethod.Read.All", "Read authentication method summaries."),
                    new("AuditLog.Read.All", "Read audit-related metadata used by some collectors.")
                ],
                OptionalPermissions:
                [
                    new("LicenseAssignment.Read.All", "Read license assignment details.", IsOptional: true),
                    new("Policy.Read.All", "Read conditional access and policy configuration.", IsOptional: true)
                ],
                RequiredPowerShellModules: [],
                AdditionalPrerequisites:
                [
                    SharedCloudAppRegistration(),
                    SharedAdminConsent(),
                    SharedCertificatePreference()
                ]),

            [WorkloadArea.Intune] = new(
                WorkloadArea.Intune,
                "Intune",
                "Managed devices, compliance, configuration, apps, and enrollment.",
                RequiredPermissions:
                [
                    new("DeviceManagementManagedDevices.Read.All", "Read managed device inventory."),
                    new("DeviceManagementConfiguration.Read.All", "Read compliance policies and configuration profiles."),
                    new("DeviceManagementApps.Read.All", "Read Intune-managed app inventory."),
                    new("DeviceManagementServiceConfig.Read.All", "Read enrollment and service configuration.")
                ],
                OptionalPermissions: [],
                RequiredPowerShellModules: [],
                AdditionalPrerequisites:
                [
                    SharedCloudAppRegistration(),
                    SharedAdminConsent(),
                    SharedCertificatePreference()
                ]),

            [WorkloadArea.ExchangeOnline] = new(
                WorkloadArea.ExchangeOnline,
                "Exchange Online",
                "Mailbox, recipient, and mail flow configuration discovery.",
                RequiredPermissions:
                [
                    new("MailboxSettings.Read", "Read mailbox settings available through Microsoft Graph."),
                    new("Reports.Read.All", "Read usage and summary reports exposed through Graph.")
                ],
                OptionalPermissions: [],
                RequiredPowerShellModules:
                [
                    new("ExchangeOnlineManagement", new Version(3, 2, 0),
                        "Exchange Online mailbox and recipient inventory via app-only PowerShell.")
                ],
                AdditionalPrerequisites:
                [
                    SharedCloudAppRegistration(),
                    SharedAdminConsent(),
                    SharedCertificatePreference(),
                    PowerShellRuntime(),
                    new(
                        "auth:exchange-manage-as-app",
                        "Exchange.ManageAsApp role",
                        "The service principal must be granted the Exchange.ManageAsApp application role.",
                        PrerequisiteCategory.Other,
                        IsRequired: true)
                ]),

            [WorkloadArea.SharePointOnline] = new(
                WorkloadArea.SharePointOnline,
                "SharePoint Online",
                "Site inventory, storage, sharing settings, and tenant site details.",
                RequiredPermissions:
                [
                    new("Sites.Read.All", "Read SharePoint site collections and site metadata.")
                ],
                OptionalPermissions:
                [
                    new("Files.Read.All", "Read library-level file metadata when required.", IsOptional: true)
                ],
                RequiredPowerShellModules:
                [
                    new("PnP.PowerShell", new Version(2, 4, 0),
                        "SharePoint tenant site details and sharing/storage enrichment.")
                ],
                AdditionalPrerequisites:
                [
                    SharedCloudAppRegistration(),
                    SharedAdminConsent(),
                    SharedCertificatePreference(),
                    PowerShellRuntime(),
                    new(
                        "role:sharepoint-admin",
                        "SharePoint Administrator role",
                        "Tenant-wide site enumeration may require the SharePoint Administrator role.",
                        PrerequisiteCategory.Other,
                        IsRequired: true)
                ]),

            [WorkloadArea.Teams] = new(
                WorkloadArea.Teams,
                "Microsoft Teams",
                "Teams, channels, and collaboration configuration discovery.",
                RequiredPermissions:
                [
                    new("Team.ReadBasic.All", "Read team definitions."),
                    new("Channel.ReadBasic.All", "Read channel definitions.")
                ],
                OptionalPermissions:
                [
                    new("SecurityEvents.Read.All", "Read Secure Score and security posture signals when enabled.", IsOptional: true)
                ],
                RequiredPowerShellModules: [],
                AdditionalPrerequisites:
                [
                    SharedCloudAppRegistration(),
                    SharedAdminConsent(),
                    SharedCertificatePreference()
                ]),

            [WorkloadArea.OnPremAD] = new(
                WorkloadArea.OnPremAD,
                "On-Premises Active Directory",
                "AD users, groups, OUs, domains, and GPO inventory from a reachable network path.",
                RequiredPermissions: [],
                OptionalPermissions: [],
                RequiredPowerShellModules: [],
                AdditionalPrerequisites:
                [
                    new(
                        "network:ldap-connectivity",
                        "LDAP network connectivity",
                        "TCP 389/636 must be reachable from the collector execution host to the target domain controllers.",
                        PrerequisiteCategory.NetworkAccess,
                        IsRequired: true),
                    new(
                        "auth:ad-read-account",
                        "Read-capable AD account or integrated auth",
                        "Use Windows Integrated authentication or a dedicated read-only AD account.",
                        PrerequisiteCategory.Other,
                        IsRequired: true)
                ]),

            [WorkloadArea.OnPremExchange] = new(
                WorkloadArea.OnPremExchange,
                "On-Premises Exchange",
                "On-premises Exchange transport and recipient discovery.",
                RequiredPermissions: [],
                OptionalPermissions: [],
                RequiredPowerShellModules: [],
                AdditionalPrerequisites:
                [
                    new(
                        "network:exchange-management",
                        "Management endpoint connectivity",
                        "The collector host must be able to reach on-premises Exchange management endpoints.",
                        PrerequisiteCategory.NetworkAccess,
                        IsRequired: true),
                    PowerShellRuntime()
                ])
        };

    public static WorkloadCapabilityDefinition GetDefinition(WorkloadArea area) => Definitions[area];

    public static IReadOnlyList<GraphPermissionRequirement> GetRequiredPermissions(IEnumerable<WorkloadArea> areas) =>
        areas
            .Where(Definitions.ContainsKey)
            .SelectMany(area => Definitions[area].RequiredPermissions)
            .GroupBy(permission => permission.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(permission => permission.Name, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<GraphPermissionRequirement> GetOptionalPermissions(IEnumerable<WorkloadArea> areas) =>
        areas
            .Where(Definitions.ContainsKey)
            .SelectMany(area => Definitions[area].OptionalPermissions)
            .GroupBy(permission => permission.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(permission => permission.Name, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<PowerShellModuleRequirement> GetRequiredModules(IEnumerable<WorkloadArea> areas) =>
        areas
            .Where(Definitions.ContainsKey)
            .SelectMany(area => Definitions[area].RequiredPowerShellModules)
            .GroupBy(module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(module => module.MinimumVersion)
                .First())
            .OrderBy(module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<WorkloadChecklistItemDefinition> BuildChecklistItems(WorkloadArea area)
    {
        var definition = GetDefinition(area);

        var items = new List<WorkloadChecklistItemDefinition>();

        items.AddRange(definition.RequiredPermissions.Select(permission =>
            new WorkloadChecklistItemDefinition(
                $"graph:{permission.Name}",
                permission.Name,
                permission.Description,
                PrerequisiteCategory.GraphPermission,
                IsRequired: true)));

        items.AddRange(definition.OptionalPermissions.Select(permission =>
            new WorkloadChecklistItemDefinition(
                $"graph:{permission.Name}",
                permission.Name,
                permission.Description,
                PrerequisiteCategory.GraphPermission,
                IsRequired: false)));

        items.AddRange(definition.RequiredPowerShellModules.Select(module =>
            new WorkloadChecklistItemDefinition(
                $"module:{module.ModuleName}",
                $"{module.ModuleName} >= {module.MinimumVersion}",
                module.Purpose,
                PrerequisiteCategory.PowerShellModule,
                IsRequired: true,
                IsAutoDetectable: true,
                IsAutoFixSupported: true)));

        items.AddRange(definition.AdditionalPrerequisites);

        return items;
    }

    private static WorkloadChecklistItemDefinition SharedCloudAppRegistration() =>
        new(
            "cloud:app-registration",
            "App registration",
            "Create or reuse an app registration for app-only collection in the customer tenant.",
            PrerequisiteCategory.Other,
            IsRequired: true);

    private static WorkloadChecklistItemDefinition SharedAdminConsent() =>
        new(
            "cloud:admin-consent",
            "Admin consent granted",
            "A tenant administrator must grant admin consent for the selected application permissions.",
            PrerequisiteCategory.AdminConsent,
            IsRequired: true);

    private static WorkloadChecklistItemDefinition SharedCertificatePreference() =>
        new(
            "cloud:certificate-preferred",
            "Certificate-based auth preferred",
            "Use a certificate whenever possible. Client secrets are supported only as a fallback.",
            PrerequisiteCategory.Certificate,
            IsRequired: true);

    private static WorkloadChecklistItemDefinition PowerShellRuntime() =>
        new(
            "tool:pwsh",
            "PowerShell 7 available",
            "The collector host must have pwsh available on PATH for PowerShell-based collection and prerequisite checks.",
            PrerequisiteCategory.PowerShellModule,
            IsRequired: true,
            IsAutoDetectable: true);
}
