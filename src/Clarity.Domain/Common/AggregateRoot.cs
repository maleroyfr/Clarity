namespace Clarity.Domain.Common;

/// <summary>
/// Aggregate roots are entities that serve as entry points to an aggregate.
/// They are responsible for enforcing domain invariants across the aggregate boundary.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
