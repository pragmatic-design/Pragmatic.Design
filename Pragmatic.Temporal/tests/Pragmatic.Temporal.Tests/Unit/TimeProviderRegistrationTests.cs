using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Extensions;
using Pragmatic.Temporal.Testing;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Tests that AddPragmaticTemporal registers TimeProvider in DI for cross-module interop.
///     Ensures IClock and TimeProvider are kept in sync when clock is replaced.
/// </summary>
public class TimeProviderRegistrationTests
{
    [Fact]
    public void AddPragmaticTemporal_RegistersTimeProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPragmaticTemporal();
        var provider = services.BuildServiceProvider();

        // Act
        var timeProvider = provider.GetService<TimeProvider>();

        // Assert
        timeProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddPragmaticTemporal_TimeProviderMatchesClock()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPragmaticTemporal();
        var provider = services.BuildServiceProvider();

        // Act
        var clock = provider.GetRequiredService<IClock>();
        var timeProvider = provider.GetRequiredService<TimeProvider>();
        var clockTime = clock.UtcNow;
        var providerTime = timeProvider.GetUtcNow();

        // Assert - both should be very close (system time)
        (providerTime - clockTime).Duration().Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void UseClock_WithTestClock_UpdatesTimeProvider()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var testClock = new TestClock(fixedTime);

        var services = new ServiceCollection();
        services.AddPragmaticTemporal();
        services.UseClock(testClock);
        var provider = services.BuildServiceProvider();

        // Act
        var timeProvider = provider.GetRequiredService<TimeProvider>();
        var clockTime = provider.GetRequiredService<IClock>().UtcNow;
        var providerTime = timeProvider.GetUtcNow();

        // Assert - both should return the test clock's time
        clockTime.Should().Be(fixedTime);
        providerTime.Should().Be(fixedTime);
    }

    [Fact]
    public void UseClock_Generic_UpdatesTimeProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPragmaticTemporal();
        services.UseClock<TestClock>();
        var provider = services.BuildServiceProvider();

        // Act
        var clock = provider.GetRequiredService<IClock>();
        var timeProvider = provider.GetRequiredService<TimeProvider>();

        // Assert - both should be backed by TestClock
        clock.Should().BeOfType<TestClock>();
        timeProvider.Should().NotBeNull();

        // TimeProvider should return the same time as IClock
        var clockTime = clock.UtcNow;
        var providerTime = timeProvider.GetUtcNow();
        (providerTime - clockTime).Duration().Should().BeLessThan(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void TestClock_GetTimeProvider_ReturnsSyncedProvider()
    {
        // Arrange
        var fixedTime = new DateTimeOffset(2025, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(fixedTime);

        // Act
        var timeProvider = clock.GetTimeProvider();
        var result = timeProvider.GetUtcNow();

        // Assert
        result.Should().Be(fixedTime);
    }

    [Fact]
    public void TestClock_GetTimeProvider_ReflectsAdvances()
    {
        // Arrange
        var clock = new TestClock(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var timeProvider = clock.GetTimeProvider();

        // Act
        clock.Advance(TimeSpan.FromHours(5));
        var result = timeProvider.GetUtcNow();

        // Assert
        result.Hour.Should().Be(5);
    }
}
