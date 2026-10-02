using Pragmatic.Testing.Assertions;
using Pragmatic.Events;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class EntityPropertyChangedTests
{
    private sealed class SampleEntity;

    [Fact]
    public void EventId_DefaultsToNonEmptyGuid()
    {
        var evt = EntityPropertyChanged<SampleEntity>.Create(1, "Name", "old", "new");

        evt.EventId.Should().NotBeEmpty();
    }

    [Fact]
    public void EventId_IsUniquePerInstance()
    {
        var first = EntityPropertyChanged<SampleEntity>.Create(1, "Name", "old", "new");
        var second = EntityPropertyChanged<SampleEntity>.Create(1, "Name", "old", "new");

        first.EventId.Should().NotBe(second.EventId);
    }

    [Fact]
    public void Create_PopulatesAllFields()
    {
        var evt = EntityPropertyChanged<SampleEntity>.Create(42, "Status", "Pending", "Active");

        evt.EntityId.Should().Be(42);
        evt.PropertyName.Should().Be("Status");
        evt.OldValue.Should().Be("Pending");
        evt.NewValue.Should().Be("Active");
    }

    [Fact]
    public void Create_AllowsNullOldAndNewValues()
    {
        var evt = EntityPropertyChanged<SampleEntity>.Create("k", "Field", null, null);

        evt.OldValue.Should().BeNull();
        evt.NewValue.Should().BeNull();
    }
}
