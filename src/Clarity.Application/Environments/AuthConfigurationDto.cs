using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Environments;

public sealed record AuthConfigurationDto(
    Guid Id,
    WorkloadArea WorkloadArea,
    AuthType AuthType,
    string? ClientId,
    string? TenantId,
    string? CertificateThumbprint,
    bool IsActive);
