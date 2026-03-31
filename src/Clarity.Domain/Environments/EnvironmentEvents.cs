using Clarity.Domain.Common;

namespace Clarity.Domain.Environments;

public sealed record EnvironmentCreatedEvent(
    Guid EnvironmentId,
    Guid CustomerId,
    string Name,
    EnvironmentType Type) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
