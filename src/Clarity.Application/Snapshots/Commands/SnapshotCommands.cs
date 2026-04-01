using Clarity.Application.Common;
using Clarity.Application.Common.Exceptions;
using Clarity.Domain.Environments;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Snapshots.Commands;

// ─── Create ───────────────────────────────────────────────────────────────────

public sealed record CreateSnapshotCommand(
    Guid CustomerId,
    Guid EnvironmentId,
    string Name,
    IReadOnlyList<WorkloadArea> WorkloadScope,
    string? Description) : ICommand<SnapshotDto>;

public sealed class CreateSnapshotHandler(ISnapshotRepository repo)
    : ICommandHandler<CreateSnapshotCommand, SnapshotDto>
{
    public async Task<SnapshotDto> Handle(CreateSnapshotCommand cmd, CancellationToken ct)
    {
        var snapshot = Snapshot.Create(
            cmd.CustomerId, cmd.EnvironmentId, cmd.Name, cmd.WorkloadScope, cmd.Description);

        await repo.AddAsync(snapshot, ct);
        await repo.SaveChangesAsync(ct);
        return snapshot.ToDto();
    }
}

// ─── Seal ─────────────────────────────────────────────────────────────────────

public sealed record SealSnapshotCommand(Guid Id) : ICommand<SnapshotDto>;

public sealed class SealSnapshotHandler(ISnapshotRepository repo)
    : ICommandHandler<SealSnapshotCommand, SnapshotDto>
{
    public async Task<SnapshotDto> Handle(SealSnapshotCommand cmd, CancellationToken ct)
    {
        var snapshot = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new NotFoundException(nameof(Snapshot), cmd.Id);

        snapshot.Seal();
        await repo.UpdateAsync(snapshot, ct);
        await repo.SaveChangesAsync(ct);
        return snapshot.ToDto();
    }
}

// ─── Mapper ───────────────────────────────────────────────────────────────────

internal static class SnapshotMappings
{
    internal static SnapshotDto ToDto(this Snapshot s) => new(
        s.Id,
        s.CustomerId,
        s.EnvironmentId,
        s.Name,
        s.Description,
        s.Status,
        s.IsImmutable,
        s.FinalizedAt,
        s.CreatedAt,
        s.WorkloadScope.Select(w => w.ToString()).ToList(),
        s.CollectorRuns.Count);
}
