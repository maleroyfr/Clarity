namespace Clarity.Domain.Common;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
