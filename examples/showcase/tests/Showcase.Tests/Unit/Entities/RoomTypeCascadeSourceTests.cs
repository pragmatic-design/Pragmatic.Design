using Pragmatic.Testing.Assertions;
using Pragmatic.Events;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests that RoomType emits EntityPropertyChanged events for cascade source properties.
/// </summary>
public class RoomTypeCascadeSourceTests
{
    [Fact]
    public void RoomType_ImplementsIHasDomainEvents()
    {
        typeof(RoomType).Should().Implement<IHasDomainEvents>();
    }

    [Fact]
    public void SetBaseRate_EmitsEntityPropertyChanged()
    {
        var roomType = RoomType.Create(250m, 10);
        roomType.ClearDomainEvents();

        roomType.SetBaseRate(300m);

        roomType.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<EntityPropertyChanged<RoomType>>()
            .Which.PropertyName.Should().Be("BaseRate");
    }

    [Fact]
    public void SetBaseRate_EventContainsOldAndNewValues()
    {
        var roomType = RoomType.Create(250m, 10);
        roomType.ClearDomainEvents();

        roomType.SetBaseRate(300m);

        var evt = roomType.DomainEvents.OfType<EntityPropertyChanged<RoomType>>().Single();
        evt.OldValue.Should().Be(250m);
        evt.NewValue.Should().Be(300m);
        evt.EntityId.Should().Be(roomType.Id);
    }

    [Fact]
    public void SetBaseRate_SameValue_NoEventEmitted()
    {
        var roomType = RoomType.Create(250m, 10);
        roomType.ClearDomainEvents();

        roomType.SetBaseRate(250m); // Same value

        roomType.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SetName_NonCascadeProperty_NoEventEmitted()
    {
        var roomType = RoomType.Create(250m, 10);
        roomType.ClearDomainEvents();

        roomType.SetName("Deluxe Suite");

        roomType.DomainEvents.Should().BeEmpty();
    }
}
