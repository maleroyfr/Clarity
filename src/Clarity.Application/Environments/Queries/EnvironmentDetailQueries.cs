using Clarity.Application.Common;
using Clarity.Application.Common.Exceptions;
using Clarity.Domain.Environments;
using DomainEnv = Clarity.Domain.Environments.Environment;

namespace Clarity.Application.Environments.Queries;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record EnvironmentDetailDto(
    Guid Id,
    string Name,
    string? Description,
    EnvironmentType Type,
    Guid? TenantId,
    string? TenantDomain,
    EnvironmentStatus Status,
    Guid CustomerId,
    IReadOnlyList<string> WorkloadAreas,
    IReadOnlyList<AuthConfigurationDto> AuthConfigurations);

// ─── Query ────────────────────────────────────────────────────────────────────

public sealed record GetEnvironmentDetailQuery(Guid Id) : IQuery<EnvironmentDetailDto>;

public sealed class GetEnvironmentDetailHandler(IEnvironmentRepository repo)
    : IQueryHandler<GetEnvironmentDetailQuery, EnvironmentDetailDto>
{
    public async Task<EnvironmentDetailDto> Handle(GetEnvironmentDetailQuery query, CancellationToken ct)
    {
        var env = await repo.GetByIdAsync(query.Id, ct)
            ?? throw new NotFoundException(nameof(DomainEnv), query.Id);

        return env.ToDetailDto();
    }
}

// ─── Mapper ───────────────────────────────────────────────────────────────────

internal static class EnvironmentDetailMappings
{
    internal static EnvironmentDetailDto ToDetailDto(this DomainEnv e) => new(
        e.Id,
        e.Name,
        e.Description,
        e.Type,
        e.TenantId,
        e.TenantDomain,
        e.Status,
        e.CustomerId,
        e.WorkloadSelections.Where(w => w.IsEnabled).Select(w => w.WorkloadArea.ToString()).ToList(),
        e.AuthConfigurations.Select(a => new AuthConfigurationDto(
            a.Id,
            a.WorkloadArea,
            a.AuthType,
            a.ClientId,
            a.TenantId,
            a.CertificateThumbprint,
            a.IsActive)).ToList());
}
