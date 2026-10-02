using Pragmatic.Persistence.Entity;

namespace Pragmatic.Tags.Tests;

public class TagBaseTests
{
    private sealed class ConcreteTag : TagBase
    {
    }

    [Fact]
    public void Defaults_Id_IsEmptyGuid()
    {
        new ConcreteTag().Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Defaults_Value_IsEmptyString()
    {
        new ConcreteTag().Value.Should().BeEmpty();
    }

    [Fact]
    public void Defaults_DisplayValue_IsEmptyString()
    {
        new ConcreteTag().DisplayValue.Should().BeEmpty();
    }

    [Fact]
    public void Defaults_Scope_IsNull()
    {
        new ConcreteTag().Scope.Should().BeNull();
    }

    [Fact]
    public void Defaults_UsageCount_IsZero()
    {
        new ConcreteTag().UsageCount.Should().Be(0);
    }

    [Fact]
    public void Defaults_CreatedAt_IsDefault()
    {
        new ConcreteTag().CreatedAt.Should().Be(default);
    }

    [Fact]
    public void Defaults_CreatedBy_IsNull()
    {
        new ConcreteTag().CreatedBy.Should().BeNull();
    }

    [Fact]
    public void PersistenceId_ForwardsToId_OnGet()
    {
        var id = Guid.NewGuid();
        var tag = new ConcreteTag { Id = id };

        tag.PersistenceId.Should().Be(id);
    }

    [Fact]
    public void PersistenceId_ForwardsToId_OnSet()
    {
        var id = Guid.NewGuid();
        var tag = new ConcreteTag { PersistenceId = id };

        tag.Id.Should().Be(id);
    }

    [Fact]
    public void PersistenceId_AndId_StayInSync()
    {
        var tag = new ConcreteTag();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        tag.Id = first;
        tag.PersistenceId.Should().Be(first);

        tag.PersistenceId = second;
        tag.Id.Should().Be(second);
    }

    [Fact]
    public void Properties_AreRoundTrippable()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var tag = new ConcreteTag
        {
            Value = "urgent",
            DisplayValue = "Urgent",
            Scope = "Reservation",
            UsageCount = 7,
            CreatedAt = createdAt,
            CreatedBy = "alice",
        };

        tag.Value.Should().Be("urgent");
        tag.DisplayValue.Should().Be("Urgent");
        tag.Scope.Should().Be("Reservation");
        tag.UsageCount.Should().Be(7);
        tag.CreatedAt.Should().Be(createdAt);
        tag.CreatedBy.Should().Be("alice");
    }

    [Fact]
    public void Type_IsAbstract()
    {
        typeof(TagBase).IsAbstract.Should().BeTrue();
    }

    [Fact]
    public void Type_ImplementsIEntityOfGuid()
    {
        typeof(TagBase).Should().BeAssignableTo<IEntity>();
        new ConcreteTag().Should().BeAssignableTo<IEntity>();
    }
}
