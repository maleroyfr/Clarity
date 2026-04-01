using Clarity.Application.Common;
using Clarity.Application.Common.Exceptions;
using Clarity.Application.Snapshots.Commands;
using Clarity.Domain.Snapshots;

namespace Clarity.Application.Snapshots.Queries;

// ─── Get by ID ────────────────────────────────────────────────────────────────

public sealed record GetSnapshotByIdQuery(Guid Id) : IQuery<SnapshotDto>;

public sealed class GetSnapshotByIdHandler(ISnapshotRepository repo)
    : IQueryHandler<GetSnapshotByIdQuery, SnapshotDto>
{
    public async Task<SnapshotDto> Handle(GetSnapshotByIdQuery query, CancellationToken ct)
    {
        var snapshot = await repo.GetByIdAsync(query.Id, ct)
            ?? throw new NotFoundException(nameof(Snapshot), query.Id);
        return snapshot.ToDto();
    }
}

// ─── List by environment ──────────────────────────────────────────────────────

public sealed record ListSnapshotsByEnvironmentQuery(Guid EnvironmentId)
    : IQuery<IReadOnlyList<SnapshotDto>>;

public sealed class ListSnapshotsByEnvironmentHandler(ISnapshotRepository repo)
    : IQueryHandler<ListSnapshotsByEnvironmentQuery, IReadOnlyList<SnapshotDto>>
{
    public async Task<IReadOnlyList<SnapshotDto>> Handle(
        ListSnapshotsByEnvironmentQuery query, CancellationToken ct)
    {
        var snapshots = await repo.GetByEnvironmentAsync(query.EnvironmentId, ct);
        return snapshots.Select(s => s.ToDto()).ToList();
    }
}
