using Pragmatic.Testing.Assertions;
using Pragmatic.Events.Tests.Fixtures;
using Xunit;

namespace Pragmatic.Events.Tests;

/// <summary>
///     Tests for <see cref="DomainEventSource" />.
/// </summary>
public class DomainEventSourceTests
{
    [Fact]
    public void NewEntity_HasNoDomainEvents()
    {
        var entity = new TestEntity();

        entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RaiseEvent_AddsEventToList()
    {
        var entity = new TestEntity();

        entity.DoSomething("test");

        entity.DomainEvents.Should().HaveCount(1);
        entity.DomainEvents[0].Should().BeOfType<TestDomainEvent>()
            .Which.Message.Should().Be("test");
    }

    [Fact]
    public void RaiseEvent_MultipleCalls_AccumulatesEvents()
    {
        var entity = new TestEntity();

        entity.DoSomething("first");
        entity.DoSomething("second");
        entity.DoSomething("third");

        entity.DomainEvents.Should().HaveCount(3);
    }

    [Fact]
    public void RaiseEvent_PreservesOrder()
    {
        var entity = new TestEntity();

        entity.DoSomething("A");
        entity.DoSomething("B");
        entity.DoSomething("C");

        entity.DomainEvents.Cast<TestDomainEvent>()
            .Select(e => e.Message)
            .Should().ContainInOrder("A", "B", "C");
    }

    [Fact]
    public void RaiseEvent_WithNull_ThrowsArgumentNullException()
    {
        var entity = new TestEntity();

        var act = () => entity.RaiseNullEvent();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RaiseEvents_AddsMultipleEvents()
    {
        var entity = new TestEntity();

        entity.DoMultipleThings("one", "two", "three");

        entity.DomainEvents.Should().HaveCount(3);
        entity.DomainEvents.Cast<TestDomainEvent>()
            .Select(e => e.Message)
            .Should().ContainInOrder("one", "two", "three");
    }

    [Fact]
    public void RaiseEvents_WithNull_ThrowsArgumentNullException()
    {
        var entity = new TestEntity();

        var act = () => entity.RaiseNullEvents();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RaiseEvents_EmptyEnumerable_DoesNotAddEvents()
    {
        var entity = new TestEntity();

        entity.DoMultipleThings();

        entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var entity = new TestEntity();
        entity.DoSomething("first");
        entity.DoSomething("second");
        entity.DomainEvents.Should().HaveCount(2);

        entity.ClearDomainEvents();

        entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ClearDomainEvents_WhenEmpty_DoesNotThrow()
    {
        var entity = new TestEntity();

        var act = () => entity.ClearDomainEvents();

        act.Should().NotThrow();
    }

    [Fact]
    public void ClearDomainEvents_AllowsNewEventsAfterClear()
    {
        var entity = new TestEntity();
        entity.DoSomething("before");
        entity.ClearDomainEvents();

        entity.DoSomething("after");

        entity.DomainEvents.Should().HaveCount(1);
        entity.DomainEvents[0].Should().BeOfType<TestDomainEvent>()
            .Which.Message.Should().Be("after");
    }

    [Fact]
    public void DomainEvents_ReturnsReadOnlyList()
    {
        var entity = new TestEntity();
        entity.DoSomething("test");

        var events = entity.DomainEvents;

        events.Should().BeAssignableTo<IReadOnlyList<IDomainEvent>>();
    }

    [Fact]
    public void DomainEvents_SetsOccurredAt()
    {
        var before = DateTimeOffset.UtcNow;
        var entity = new TestEntity();

        entity.DoSomething("test");

        var after = DateTimeOffset.UtcNow;
        entity.DomainEvents[0].OccurredAt.Should().BeOnOrAfter(before);
        entity.DomainEvents[0].OccurredAt.Should().BeOnOrBefore(after);
    }
}
