using Clarity.Domain.Common;
using Clarity.SharedContracts.Enums;

namespace Clarity.Domain.Environments;

/// <summary>
/// Stores the auth configuration for a workload within an environment.
/// Never stores plaintext secrets — only references (thumbprints, KV URIs, DPAPI refs).
/// </summary>
public sealed class AuthConfiguration : Entity
{
    public Guid EnvironmentId { get; private set; }
    public WorkloadArea WorkloadArea { get; private set; }
    public AuthType AuthType { get; private set; }

    /// <summary>Application (client) ID of the Azure AD app registration.</summary>
    public string? ClientId { get; private set; }

    /// <summary>Directory (tenant) ID.</summary>
    public string? TenantId { get; private set; }

    /// <summary>Certificate thumbprint from the local Windows Certificate Store.</summary>
    public string? CertificateThumbprint { get; private set; }

    /// <summary>
    /// Opaque reference to the secret location — DPAPI-encrypted file path
    /// or Azure Key Vault secret URI. NEVER a plaintext secret.
    /// </summary>
    public string? SecretReference { get; private set; }

    public bool IsActive { get; private set; }

    private AuthConfiguration() { } // EF Core

    public static AuthConfiguration CreateCertificate(
        Guid environmentId,
        WorkloadArea workloadArea,
        string clientId,
        string tenantId,
        string certificateThumbprint)
    {
        return new AuthConfiguration
        {
            EnvironmentId = environmentId,
            WorkloadArea = workloadArea,
            AuthType = AuthType.Certificate,
            ClientId = clientId,
            TenantId = tenantId,
            CertificateThumbprint = certificateThumbprint,
            IsActive = true
        };
    }

    public static AuthConfiguration CreateClientSecret(
        Guid environmentId,
        WorkloadArea workloadArea,
        string clientId,
        string tenantId,
        string secretReference)
    {
        return new AuthConfiguration
        {
            EnvironmentId = environmentId,
            WorkloadArea = workloadArea,
            AuthType = AuthType.ClientSecret,
            ClientId = clientId,
            TenantId = tenantId,
            SecretReference = secretReference,
            IsActive = true
        };
    }

    public static AuthConfiguration CreateWindowsIntegrated(Guid environmentId, WorkloadArea workloadArea)
    {
        return new AuthConfiguration
        {
            EnvironmentId = environmentId,
            WorkloadArea = workloadArea,
            AuthType = AuthType.WindowsIntegrated,
            IsActive = true
        };
    }

    public void Deactivate() { IsActive = false; MarkUpdated(); }
    public void Activate() { IsActive = true; MarkUpdated(); }
}
