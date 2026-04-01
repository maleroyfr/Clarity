using Clarity.Application.Common;
using Clarity.Application.Customers.Commands;
using Clarity.Domain.Customers;

namespace Clarity.Application.Customers.Queries;

// ─── Get by ID ────────────────────────────────────────────────────────────────

public sealed record GetCustomerByIdQuery(Guid Id) : IQuery<CustomerDto>;

public sealed class GetCustomerByIdHandler(ICustomerRepository repo)
    : IQueryHandler<GetCustomerByIdQuery, CustomerDto>
{
    public async Task<CustomerDto> Handle(GetCustomerByIdQuery query, CancellationToken ct)
    {
        var customer = await repo.GetByIdAsync(query.Id, ct)
            ?? throw new Common.Exceptions.NotFoundException(nameof(Customer), query.Id);
        return customer.ToDto();
    }
}

// ─── List all ─────────────────────────────────────────────────────────────────

public sealed record ListCustomersQuery(bool IncludeArchived = false) : IQuery<IReadOnlyList<CustomerDto>>;

public sealed class ListCustomersHandler(ICustomerRepository repo)
    : IQueryHandler<ListCustomersQuery, IReadOnlyList<CustomerDto>>
{
    public async Task<IReadOnlyList<CustomerDto>> Handle(ListCustomersQuery query, CancellationToken ct)
    {
        var customers = await repo.GetAllAsync(query.IncludeArchived, ct);
        return customers.Select(CustomerMappings.ToDto).ToList();
    }
}
