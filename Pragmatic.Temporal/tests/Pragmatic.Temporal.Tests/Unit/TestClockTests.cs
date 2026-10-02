using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Testing;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

public class TestClockTests
{
    [Fact]
    public void Constructor_WithoutArguments_SetsDefaultTime()
    {
        var clock = new TestClock();

        // Default is 2024-01-15 12:00:00 UTC
        clock.UtcNow.Year.Should().Be(2024);
        clock.UtcNow.Month.Should().Be(1);
        clock.UtcNow.Day.Should().Be(15);
    }

    [Fact]
    public void Constructor_WithSpecificTime_SetsTime()
    {
        var time = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var clock = new TestClock(time);

        clock.UtcNow.Should().Be(time);
    }

    [Fact]
    public void Set_UpdatesCurrentTime()
    {
        var clock = new TestClock();
        var newTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        clock.Set(newTime);

        clock.UtcNow.Should().Be(newTime);
    }

    [Fact]
    public void Advance_MovesTimeForward()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        clock.Advance(TimeSpan.FromHours(2));

        clock.UtcNow.Hour.Should().Be(14);
    }

    [Fact]
    public void AdvanceDays_MovesTimeForwardByDays()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        clock.AdvanceDays(5);

        clock.UtcNow.Day.Should().Be(6);
    }

    [Fact]
    public void AutoAdvance_WhenEnabled_AdvancesOnEachRead()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero))
        {
            AutoAdvance = true,
            AutoAdvanceAmount = TimeSpan.FromSeconds(1)
        };

        var time1 = clock.UtcNow;
        var time2 = clock.UtcNow;

        (time2 - time1).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SetDateTime_FluentApi_SetsCorrectTime()
    {
        var clock = new TestClock()
            .SetDateTime(2024, 6, 15, 14, 30);

        clock.UtcNow.Year.Should().Be(2024);
        clock.UtcNow.Month.Should().Be(6);
        clock.UtcNow.Day.Should().Be(15);
        clock.UtcNow.Hour.Should().Be(14);
        clock.UtcNow.Minute.Should().Be(30);
    }

    [Fact]
    public void GetTimeProvider_ReturnsWorkingProvider()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero));

        var provider = clock.GetTimeProvider();
        var time = provider.GetUtcNow();

        time.Should().Be(clock.UtcNow);
    }
}