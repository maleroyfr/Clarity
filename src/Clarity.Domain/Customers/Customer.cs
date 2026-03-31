using Clarity.Domain.Common;

namespace Clarity.Domain.Customers;

/// <summary>
/// Customer is the top-level aggregate. A customer represents a client organisation
/// being audited. It owns Environments and is the billing/logical boundary for all data.
/// </summary>
public sealed class Customer : AggregateRoot
{
    private readonly List<Tag> _tags = [];

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsArchived { get; private set; }
    public IReadOnlyList<Tag> Tags => _tags.AsReadOnly();

    private Customer() { } // EF Core

    public static Customer Create(string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var customer = new Customer
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            IsArchived = false
        };

        customer.AddDomainEvent(new CustomerCreatedEvent(customer.Id, customer.Name));
        return customer;
    }

    public void Update(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        MarkUpdated();
    }

    public void Archive()
    {
        if (IsArchived) return;
        IsArchived = true;
        MarkUpdated();
        AddDomainEvent(new CustomerArchivedEvent(Id));
    }

    public void Restore()
    {
        if (!IsArchived) return;
        IsArchived = false;
        MarkUpdated();
    }

    public void AddTag(Tag tag)
    {
        if (!_tags.Contains(tag))
            _tags.Add(tag);
    }

    public void RemoveTag(Tag tag) => _tags.Remove(tag);
}
