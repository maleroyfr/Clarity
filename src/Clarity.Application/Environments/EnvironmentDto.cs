using Clarity.Domain.Environments;

namespace Clarity.Application.Environments;

public sealed record EnvironmentDto(
    Guid Id,
    string Name,
    string? Description,
    EnvironmentType Type,
    Guid? TenantId,
    string? TenantDomain,
    EnvironmentStatus Status,
    Guid CustomerId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> WorkloadAreas,
    bool IsArchived);
