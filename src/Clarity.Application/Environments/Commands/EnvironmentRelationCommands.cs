using Clarity.Application.Common;
using Clarity.Application.Common.Exceptions;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;

namespace Clarity.Application.Environments.Commands;

// ─── Create ───────────────────────────────────────────────────────────────────

public sealed record CreateRelationCommand(
    Guid CustomerId,
    Guid SourceId,
    Guid TargetId,
    RelationType Type,
    RelationDirection Direction,
    string? Notes) : ICommand<EnvironmentRelationDto>;

public sealed class CreateRelationHandler(IEnvironmentRelationRepository repo)
    : ICommandHandler<CreateRelationCommand, EnvironmentRelationDto>
{
    public async Task<EnvironmentRelationDto> Handle(CreateRelationCommand cmd, CancellationToken ct)
    {
        var relation = EnvironmentRelation.Create(
            cmd.CustomerId, cmd.SourceId, cmd.TargetId, cmd.Type, cmd.Direction, cmd.Notes);

        await repo.AddAsync(relation, ct);
        await repo.SaveChangesAsync(ct);
        return relation.ToDto();
    }
}

// ─── Update ───────────────────────────────────────────────────────────────────

public sealed record UpdateRelationCommand(
    Guid Id,
    RelationType Type,
    RelationDirection Direction,
    string? Notes) : ICommand;

public sealed class UpdateRelationHandler(IEnvironmentRelationRepository repo)
    : ICommandHandler<UpdateRelationCommand>
{
    public async Task Handle(UpdateRelationCommand cmd, CancellationToken ct)
    {
        var relation = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new NotFoundException(nameof(EnvironmentRelation), cmd.Id);

        relation.Update(cmd.Type, cmd.Direction, cmd.Notes);
        await repo.UpdateAsync(relation, ct);
        await repo.SaveChangesAsync(ct);
    }
}

// ─── Delete ───────────────────────────────────────────────────────────────────

public sealed record DeleteRelationCommand(Guid Id) : ICommand;

public sealed class DeleteRelationHandler(IEnvironmentRelationRepository repo)
    : ICommandHandler<DeleteRelationCommand>
{
    public async Task Handle(DeleteRelationCommand cmd, CancellationToken ct)
    {
        var relation = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new NotFoundException(nameof(EnvironmentRelation), cmd.Id);

        await repo.DeleteAsync(relation.Id, ct);
        await repo.SaveChangesAsync(ct);
    }
}

// ─── Mapper ───────────────────────────────────────────────────────────────────

internal static class EnvironmentRelationMappings
{
    internal static EnvironmentRelationDto ToDto(this EnvironmentRelation r) => new(
        r.Id,
        r.CustomerId,
        r.SourceEnvironmentId,
        r.TargetEnvironmentId,
        r.RelationType,
        r.Direction,
        r.Notes,
        r.CreatedAt);
}
