using Clarity.Domain.Common;

namespace Clarity.Domain.Customers;

public sealed record CustomerCreatedEvent(Guid CustomerId, string Name) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

public sealed record CustomerArchivedEvent(Guid CustomerId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
