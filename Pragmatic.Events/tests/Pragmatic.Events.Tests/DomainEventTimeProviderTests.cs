using Pragmatic.Testing.Assertions;
using Pragmatic.Events.Tests.Fixtures;
using Xunit;

namespace Pragmatic.Events.Tests;

/// <summary>
///     Tests that domain events support TimeProvider for deterministic timestamps.
///     Validates the recommended pattern of injecting TimeProvider into entities.
/// </summary>
public class DomainEventTimeProviderTests
{
    [Fact]
    public void RaiseEvent_WithFakeTimeProvider_UsesProvidedTimestamp()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var entity = new TimeProviderAwareEntity(timeProvider);

        // Act
        entity.DoSomething("test");

        // Assert
        entity.DomainEvents[0].OccurredAt.Should().Be(fixedTime);
    }

    [Fact]
    public void RaiseEvent_WithAdvancedTime_ReflectsTimeProgression()
    {
        // Arrange
        var initialTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(initialTime);
        var entity = new TimeProviderAwareEntity(timeProvider);

        // Act - raise events at different times
        entity.DoSomething("first");

        timeProvider.Advance(TimeSpan.FromHours(1));
        entity.DoSomething("second");

        timeProvider.Advance(TimeSpan.FromHours(2));
        entity.DoSomething("third");

        // Assert
        entity.DomainEvents[0].OccurredAt.Should().Be(initialTime);
        entity.DomainEvents[1].OccurredAt.Should().Be(initialTime.AddHours(1));
        entity.DomainEvents[2].OccurredAt.Should().Be(initialTime.AddHours(3));
    }

    [Fact]
    public void RaiseEvent_WithoutTimeProvider_FallsBackToSystem()
    {
        // Arrange - no TimeProvider means system clock
        var before = DateTimeOffset.UtcNow;
        var entity = new TimeProviderAwareEntity();

        // Act
        entity.DoSomething("test");

        // Assert
        var after = DateTimeOffset.UtcNow;
        entity.DomainEvents[0].OccurredAt.Should().BeOnOrAfter(before);
        entity.DomainEvents[0].OccurredAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void DomainEventRecord_CanBeCreatedWithExplicitTimestamp()
    {
        // Arrange - direct construction with explicit timestamp (always works)
        var fixedTime = new DateTimeOffset(2025, 7, 4, 12, 0, 0, TimeSpan.Zero);

        // Act
        var evt = new TestDomainEvent("manual") { OccurredAt = fixedTime };

        // Assert
        evt.OccurredAt.Should().Be(fixedTime);
    }
}
