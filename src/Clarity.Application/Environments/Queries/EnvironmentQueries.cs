using Clarity.Application.Common;
using Clarity.Application.Common.Exceptions;
using Clarity.Application.Environments.Commands;
using Clarity.Domain.Environments;

// Alias to avoid ambiguity with System.Environment
using DomainEnv = Clarity.Domain.Environments.Environment;

namespace Clarity.Application.Environments.Queries;

// ─── Get by ID ────────────────────────────────────────────────────────────────

public sealed record GetEnvironmentByIdQuery(Guid Id) : IQuery<EnvironmentDto>;

public sealed class GetEnvironmentByIdHandler(IEnvironmentRepository repo)
    : IQueryHandler<GetEnvironmentByIdQuery, EnvironmentDto>
{
    public async Task<EnvironmentDto> Handle(GetEnvironmentByIdQuery query, CancellationToken ct)
    {
        var env = await repo.GetByIdAsync(query.Id, ct)
            ?? throw new NotFoundException(nameof(DomainEnv), query.Id);
        return env.ToDto();
    }
}

// ─── List by customer ─────────────────────────────────────────────────────────

public sealed record ListEnvironmentsByCustomerQuery(
    Guid CustomerId,
    bool IncludeArchived = false) : IQuery<IReadOnlyList<EnvironmentDto>>;

public sealed class ListEnvironmentsByCustomerHandler(IEnvironmentRepository repo)
    : IQueryHandler<ListEnvironmentsByCustomerQuery, IReadOnlyList<EnvironmentDto>>
{
    public async Task<IReadOnlyList<EnvironmentDto>> Handle(
        ListEnvironmentsByCustomerQuery query, CancellationToken ct)
    {
        var environments = await repo.GetByCustomerAsync(query.CustomerId, ct);
        var filtered = query.IncludeArchived
            ? environments
            : environments.Where(e => e.Status != EnvironmentStatus.Archived).ToList();
        return filtered.Select(e => e.ToDto()).ToList();
    }
}
