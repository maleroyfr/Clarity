using Clarity.Application.Common;
using Clarity.Domain.Customers;
using FluentValidation;
using MediatR;

namespace Clarity.Application.Customers.Commands;

// ─── Create ───────────────────────────────────────────────────────────────────

public sealed record CreateCustomerCommand(string Name, string? Description) : ICommand<CustomerDto>;

public sealed class CreateCustomerValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}

public sealed class CreateCustomerHandler(ICustomerRepository repo)
    : ICommandHandler<CreateCustomerCommand, CustomerDto>
{
    public async Task<CustomerDto> Handle(CreateCustomerCommand cmd, CancellationToken ct)
    {
        var customer = Customer.Create(cmd.Name, cmd.Description);
        await repo.AddAsync(customer, ct);
        await repo.SaveChangesAsync(ct);
        return customer.ToDto();
    }
}

// ─── Update ───────────────────────────────────────────────────────────────────

public sealed record UpdateCustomerCommand(Guid Id, string Name, string? Description) : ICommand<CustomerDto>;

public sealed class UpdateCustomerValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}

public sealed class UpdateCustomerHandler(ICustomerRepository repo)
    : ICommandHandler<UpdateCustomerCommand, CustomerDto>
{
    public async Task<CustomerDto> Handle(UpdateCustomerCommand cmd, CancellationToken ct)
    {
        var customer = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new Common.Exceptions.NotFoundException(nameof(Customer), cmd.Id);

        customer.Update(cmd.Name, cmd.Description);
        await repo.UpdateAsync(customer, ct);
        await repo.SaveChangesAsync(ct);
        return customer.ToDto();
    }
}

// ─── Archive ──────────────────────────────────────────────────────────────────

public sealed record ArchiveCustomerCommand(Guid Id) : ICommand;

public sealed class ArchiveCustomerHandler(ICustomerRepository repo)
    : ICommandHandler<ArchiveCustomerCommand>
{
    public async Task Handle(ArchiveCustomerCommand cmd, CancellationToken ct)
    {
        var customer = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new Common.Exceptions.NotFoundException(nameof(Customer), cmd.Id);

        customer.Archive();
        await repo.UpdateAsync(customer, ct);
        await repo.SaveChangesAsync(ct);
    }
}

// ─── Restore ──────────────────────────────────────────────────────────────────

public sealed record RestoreCustomerCommand(Guid Id) : ICommand;

public sealed class RestoreCustomerHandler(ICustomerRepository repo)
    : ICommandHandler<RestoreCustomerCommand>
{
    public async Task Handle(RestoreCustomerCommand cmd, CancellationToken ct)
    {
        var customer = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new Common.Exceptions.NotFoundException(nameof(Customer), cmd.Id);

        customer.Restore();
        await repo.UpdateAsync(customer, ct);
        await repo.SaveChangesAsync(ct);
    }
}

// ─── Mapper ───────────────────────────────────────────────────────────────────

internal static class CustomerMappings
{
    internal static CustomerDto ToDto(this Customer c) => new(
        c.Id, c.Name, c.Description, c.IsArchived, c.CreatedAt, c.UpdatedAt,
        c.Tags.Select(t => t.ToString()).ToList());
}
