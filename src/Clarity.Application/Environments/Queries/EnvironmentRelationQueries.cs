using Clarity.Application.Common;
using Clarity.Application.Environments.Commands;
using Clarity.Domain.Environments;

namespace Clarity.Application.Environments.Queries;

// ─── List by customer ─────────────────────────────────────────────────────────

public sealed record ListRelationsByCustomerQuery(Guid CustomerId)
    : IQuery<IReadOnlyList<EnvironmentRelationDto>>;

public sealed class ListRelationsByCustomerHandler(IEnvironmentRelationRepository repo)
    : IQueryHandler<ListRelationsByCustomerQuery, IReadOnlyList<EnvironmentRelationDto>>
{
    public async Task<IReadOnlyList<EnvironmentRelationDto>> Handle(
        ListRelationsByCustomerQuery query, CancellationToken ct)
    {
        var relations = await repo.GetByCustomerAsync(query.CustomerId, ct);
        return relations.Select(r => r.ToDto()).ToList();
    }
}
