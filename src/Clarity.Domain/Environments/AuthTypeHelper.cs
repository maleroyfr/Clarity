using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Environments;

/// <summary>
/// Maps each workload area to the authentication types that are technically valid for it.
/// Cloud workloads use Azure AD app registrations; on-premises workloads use Windows/LDAP auth.
/// </summary>
public static class AuthTypeHelper
{
    public static IReadOnlyList<AuthType> GetValidAuthTypes(WorkloadArea area) => area switch
    {
        WorkloadArea.EntraId => CloudAuthTypes,
        WorkloadArea.Intune => CloudAuthTypes,
        WorkloadArea.ExchangeOnline => CloudAuthTypes,
        WorkloadArea.SharePointOnline => CloudAuthTypes,
        WorkloadArea.Teams => CloudAuthTypes,
        WorkloadArea.OnPremAD => OnPremAuthTypes,
        WorkloadArea.OnPremExchange => OnPremAuthTypes,
        _ => CloudAuthTypes
    };

    public static bool IsCloudWorkload(WorkloadArea area) =>
        area is WorkloadArea.EntraId or WorkloadArea.Intune or WorkloadArea.ExchangeOnline
            or WorkloadArea.SharePointOnline or WorkloadArea.Teams;

    public static bool IsOnPremWorkload(WorkloadArea area) =>
        area is WorkloadArea.OnPremAD or WorkloadArea.OnPremExchange;

    public static string GetAuthTypeDisplayName(AuthType authType) => authType switch
    {
        AuthType.Certificate => "Certificate",
        AuthType.ClientSecret => "Client Secret",
        AuthType.WindowsIntegrated => "Windows Integrated",
        AuthType.ServiceAccount => "Service Account",
        _ => authType.ToString()
    };

    public static string GetAuthTypeDescription(AuthType authType) => authType switch
    {
        AuthType.Certificate => "App registration with certificate (recommended for production)",
        AuthType.ClientSecret => "App registration with client secret (fallback)",
        AuthType.WindowsIntegrated => "Use current Windows session credentials (Kerberos/NTLM)",
        AuthType.ServiceAccount => "Dedicated read-only account with username and password",
        _ => string.Empty
    };

    private static readonly IReadOnlyList<AuthType> CloudAuthTypes = [AuthType.Certificate, AuthType.ClientSecret];
    private static readonly IReadOnlyList<AuthType> OnPremAuthTypes = [AuthType.WindowsIntegrated, AuthType.ServiceAccount];
}
