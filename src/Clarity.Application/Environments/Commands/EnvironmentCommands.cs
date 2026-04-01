using Clarity.Application.Common;
using Clarity.Application.Common.Exceptions;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using FluentValidation;

// Alias to avoid ambiguity with System.Environment
using DomainEnv = Clarity.Domain.Environments.Environment;

namespace Clarity.Application.Environments.Commands;

// ─── Create ───────────────────────────────────────────────────────────────────

public sealed record CreateEnvironmentCommand(
    Guid CustomerId,
    string Name,
    EnvironmentType Type,
    string? Description,
    Guid? TenantId,
    string? TenantDomain,
    IReadOnlyList<string> WorkloadAreas) : ICommand<EnvironmentDto>;

public sealed class CreateEnvironmentValidator : AbstractValidator<CreateEnvironmentCommand>
{
    public CreateEnvironmentValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}

public sealed class CreateEnvironmentHandler(IEnvironmentRepository repo)
    : ICommandHandler<CreateEnvironmentCommand, EnvironmentDto>
{
    public async Task<EnvironmentDto> Handle(CreateEnvironmentCommand cmd, CancellationToken ct)
    {
        var env = DomainEnv.Create(cmd.CustomerId, cmd.Name, cmd.Type, cmd.Description, cmd.TenantId, cmd.TenantDomain);

        if (cmd.WorkloadAreas.Count > 0)
        {
            var workloads = cmd.WorkloadAreas
                .Select(w => Enum.Parse<WorkloadArea>(w, ignoreCase: true));
            env.SetWorkloads(workloads);
        }

        await repo.AddAsync(env, ct);
        await repo.SaveChangesAsync(ct);
        return env.ToDto();
    }
}

// ─── Update ───────────────────────────────────────────────────────────────────

public sealed record UpdateEnvironmentCommand(
    Guid Id,
    string Name,
    string? Description,
    string? TenantDomain) : ICommand<EnvironmentDto>;

public sealed class UpdateEnvironmentValidator : AbstractValidator<UpdateEnvironmentCommand>
{
    public UpdateEnvironmentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}

public sealed class UpdateEnvironmentHandler(IEnvironmentRepository repo)
    : ICommandHandler<UpdateEnvironmentCommand, EnvironmentDto>
{
    public async Task<EnvironmentDto> Handle(UpdateEnvironmentCommand cmd, CancellationToken ct)
    {
        var env = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new NotFoundException(nameof(DomainEnv), cmd.Id);

        env.Update(cmd.Name, cmd.Description, cmd.TenantDomain);
        await repo.UpdateAsync(env, ct);
        await repo.SaveChangesAsync(ct);
        return env.ToDto();
    }
}

// ─── Archive ──────────────────────────────────────────────────────────────────

public sealed record ArchiveEnvironmentCommand(Guid Id) : ICommand;

public sealed class ArchiveEnvironmentHandler(IEnvironmentRepository repo)
    : ICommandHandler<ArchiveEnvironmentCommand>
{
    public async Task Handle(ArchiveEnvironmentCommand cmd, CancellationToken ct)
    {
        var env = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new NotFoundException(nameof(DomainEnv), cmd.Id);

        env.Archive();
        await repo.UpdateAsync(env, ct);
        await repo.SaveChangesAsync(ct);
    }
}

// ─── SetWorkloads ─────────────────────────────────────────────────────────────

public sealed record SetEnvironmentWorkloadsCommand(
    Guid Id,
    IReadOnlyList<WorkloadArea> WorkloadAreas) : ICommand<EnvironmentDto>;

public sealed class SetEnvironmentWorkloadsHandler(IEnvironmentRepository repo)
    : ICommandHandler<SetEnvironmentWorkloadsCommand, EnvironmentDto>
{
    public async Task<EnvironmentDto> Handle(SetEnvironmentWorkloadsCommand cmd, CancellationToken ct)
    {
        var env = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new NotFoundException(nameof(DomainEnv), cmd.Id);

        env.SetWorkloads(cmd.WorkloadAreas);
        await repo.UpdateAsync(env, ct);
        await repo.SaveChangesAsync(ct);
        return env.ToDto();
    }
}

// ─── Mapper ───────────────────────────────────────────────────────────────────

internal static class EnvironmentMappings
{
    internal static EnvironmentDto ToDto(this DomainEnv e) => new(
        e.Id,
        e.Name,
        e.Description,
        e.Type,
        e.TenantId,
        e.TenantDomain,
        e.Status,
        e.CustomerId,
        e.CreatedAt,
        e.WorkloadSelections.Where(w => w.IsEnabled).Select(w => w.WorkloadArea.ToString()).ToList(),
        e.Status == EnvironmentStatus.Archived);
}
