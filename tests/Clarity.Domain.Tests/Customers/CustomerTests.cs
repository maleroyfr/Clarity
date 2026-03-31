using Clarity.Domain.Customers;
using Clarity.Domain.Common;
using FluentAssertions;

namespace Clarity.Domain.Tests.Customers;

public sealed class CustomerTests
{
    [Fact]
    public void Create_WithValidName_ShouldSucceed()
    {
        var customer = Customer.Create("Contoso Ltd", "A test customer");

        customer.Name.Should().Be("Contoso Ltd");
        customer.Description.Should().Be("A test customer");
        customer.IsArchived.Should().BeFalse();
        customer.DomainEvents.Should().ContainSingle(e => e is CustomerCreatedEvent);
    }

    [Fact]
    public void Create_TrimsName()
    {
        var customer = Customer.Create("  Contoso  ");
        customer.Name.Should().Be("Contoso");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ShouldThrow(string name)
    {
        var act = () => Customer.Create(name);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Archive_ShouldSetIsArchivedTrue()
    {
        var customer = Customer.Create("Contoso");
        customer.Archive();

        customer.IsArchived.Should().BeTrue();
        customer.DomainEvents.Should().Contain(e => e is CustomerArchivedEvent);
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_ShouldNotDuplicateEvent()
    {
        var customer = Customer.Create("Contoso");
        customer.Archive();
        customer.ClearDomainEvents();
        customer.Archive();

        customer.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Restore_ShouldSetIsArchivedFalse()
    {
        var customer = Customer.Create("Contoso");
        customer.Archive();
        customer.Restore();

        customer.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Update_ShouldChangeName()
    {
        var customer = Customer.Create("Old Name");
        customer.Update("New Name", "New desc");

        customer.Name.Should().Be("New Name");
        customer.Description.Should().Be("New desc");
    }

    [Fact]
    public void AddTag_ShouldAddTag()
    {
        var customer = Customer.Create("Contoso");
        customer.AddTag(new Tag("region", "emea"));

        customer.Tags.Should().ContainSingle(t => t.Key == "region" && t.Value == "emea");
    }

    [Fact]
    public void AddTag_Duplicate_ShouldNotAddTwice()
    {
        var customer = Customer.Create("Contoso");
        customer.AddTag(new Tag("region", "emea"));
        customer.AddTag(new Tag("region", "emea"));

        customer.Tags.Should().HaveCount(1);
    }
}
