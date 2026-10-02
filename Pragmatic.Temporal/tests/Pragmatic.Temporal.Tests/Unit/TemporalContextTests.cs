using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Testing;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

public class TemporalContextTests
{
    #region Factory Methods

    [Fact]
    public void Utc_CreatesContextWithUtcTimezones()
    {
        var context = TemporalContext.Utc();

        context.ClientTimeZone.Should().Be(TimeZoneInfo.Utc);
        context.BusinessTimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void ForZone_CreatesContextWithSpecifiedTimezone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var context = TemporalContext.ForZone(zone);

        context.ClientTimeZone.Should().Be(zone);
        context.BusinessTimeZone.Should().Be(zone);
    }

    #endregion

    #region Current Time

    [Fact]
    public void UtcNow_ReturnsClockTime()
    {
        var fixedTime = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(fixedTime);
        var context = TemporalContext.Utc(clock);

        context.UtcNow.Should().Be(fixedTime);
    }

    [Fact]
    public void ClientNow_ReturnsTimeInClientTimezone()
    {
        var fixedUtc = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(fixedUtc);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

        var context = new TemporalContext
        {
            Clock = clock,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        // CEST is UTC+2 in summer
        context.ClientNow.LocalDateTime.Hour.Should().Be(14);
    }

    #endregion

    #region DST - Non-Existent Time (Spring Forward)

    [Fact]
    public void ClientToUtc_NonExistentTime_ShiftForward_SkipsToValidTime()
    {
        // In US Eastern, 2:30 AM on March 10, 2024 doesn't exist (clocks jump 2:00 -> 3:00)
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var context = new TemporalContext
        {
            Clock = SystemClock.Instance,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        // 2:30 AM doesn't exist - should shift to 3:00 AM
        var nonExistentTime = new DateTime(2024, 3, 10, 2, 30, 0);

        var result = context.ClientToUtc(nonExistentTime,
            NonExistentTimePolicy.ShiftForward,
            AmbiguousTimePolicy.UseStandardTime);

        // 3:00 AM EDT = 7:00 AM UTC (EDT is UTC-4)
        result.Hour.Should().Be(7);
    }

    [Fact]
    public void ClientToUtc_NonExistentTime_ThrowException_Throws()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var context = new TemporalContext
        {
            Clock = SystemClock.Instance,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        var nonExistentTime = new DateTime(2024, 3, 10, 2, 30, 0);

        var act = () => context.ClientToUtc(nonExistentTime,
            NonExistentTimePolicy.ThrowException,
            AmbiguousTimePolicy.UseStandardTime);

        act.Should().Throw<NonExistentTimeException>()
            .Which.LocalTime.Should().Be(nonExistentTime);
    }

    [Fact]
    public void ClientToUtc_DefaultPolicy_UsesShiftForward()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var context = new TemporalContext
        {
            Clock = SystemClock.Instance,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        var nonExistentTime = new DateTime(2024, 3, 10, 2, 30, 0);

        // Default should not throw
        var act = () => context.ClientToUtc(nonExistentTime);

        act.Should().NotThrow();
    }

    #endregion

    #region DST - Ambiguous Time (Fall Back)

    [Fact]
    public void ClientToUtc_AmbiguousTime_UseStandardTime_UsesLaterOffset()
    {
        // In US Eastern, 1:30 AM on Nov 3, 2024 occurs twice (clocks fall back 2:00 -> 1:00)
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var context = new TemporalContext
        {
            Clock = SystemClock.Instance,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        var ambiguousTime = new DateTime(2024, 11, 3, 1, 30, 0);

        var result = context.ClientToUtc(ambiguousTime,
            NonExistentTimePolicy.ShiftForward,
            AmbiguousTimePolicy.UseStandardTime);

        // Standard time (EST) is UTC-5, so 1:30 AM EST = 6:30 AM UTC
        result.Hour.Should().Be(6);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void ClientToUtc_AmbiguousTime_UseDaylightTime_UsesEarlierOffset()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var context = new TemporalContext
        {
            Clock = SystemClock.Instance,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        var ambiguousTime = new DateTime(2024, 11, 3, 1, 30, 0);

        var result = context.ClientToUtc(ambiguousTime,
            NonExistentTimePolicy.ShiftForward,
            AmbiguousTimePolicy.UseDaylightTime);

        // Daylight time (EDT) is UTC-4, so 1:30 AM EDT = 5:30 AM UTC
        result.Hour.Should().Be(5);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void ClientToUtc_AmbiguousTime_ThrowException_Throws()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var context = new TemporalContext
        {
            Clock = SystemClock.Instance,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        var ambiguousTime = new DateTime(2024, 11, 3, 1, 30, 0);

        var act = () => context.ClientToUtc(ambiguousTime,
            NonExistentTimePolicy.ShiftForward,
            AmbiguousTimePolicy.ThrowException);

        act.Should().Throw<AmbiguousTimeException>()
            .Which.LocalTime.Should().Be(ambiguousTime);
    }

    #endregion

    #region Normal Time (No DST Issues)

    [Fact]
    public void ClientToUtc_NormalTime_ConvertsCorrectly()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var context = new TemporalContext
        {
            Clock = SystemClock.Instance,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        // June 15, 2024 - clearly in EDT (no DST transition)
        var normalTime = new DateTime(2024, 6, 15, 10, 30, 0);

        var result = context.ClientToUtc(normalTime);

        // 10:30 AM EDT = 2:30 PM UTC (EDT is UTC-4)
        result.Hour.Should().Be(14);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void BusinessToUtc_NormalTime_ConvertsCorrectly()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        var context = new TemporalContext
        {
            Clock = SystemClock.Instance,
            ClientTimeZone = TimeZoneInfo.Utc,
            BusinessTimeZone = zone
        };

        // June 15, 2024 - CEST (UTC+2)
        var normalTime = new DateTime(2024, 6, 15, 14, 0, 0);

        var result = context.BusinessToUtc(normalTime);

        // 2:00 PM CEST = 12:00 PM UTC
        result.Hour.Should().Be(12);
    }

    #endregion

    #region Date Range Helpers

    [Fact]
    public void ClientStartOfDay_ReturnsUtcMidnight()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var clock = new TestClock(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var context = new TemporalContext
        {
            Clock = clock,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        var date = new LocalDate(2024, 6, 15);
        var result = context.ClientStartOfDay(date);

        // Midnight EDT = 4:00 AM UTC (EDT is UTC-4)
        result.Hour.Should().Be(4);
        result.Minute.Should().Be(0);
    }

    [Fact]
    public void ClientEndOfDay_ReturnsNextDayMidnight()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var clock = new TestClock(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var context = new TemporalContext
        {
            Clock = clock,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        var date = new LocalDate(2024, 6, 15);
        var result = context.ClientEndOfDay(date);

        // Midnight June 16 EDT = 4:00 AM UTC June 16
        result.Day.Should().Be(16);
        result.Hour.Should().Be(4);
    }

    [Fact]
    public void ClientTodayRange_ReturnsCorrectRange()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var clock = new TestClock(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero));
        var context = new TemporalContext
        {
            Clock = clock,
            ClientTimeZone = zone,
            BusinessTimeZone = TimeZoneInfo.Utc
        };

        var (start, end) = context.ClientTodayRange;

        end.Should().BeAfter(start);
        (end - start).TotalHours.Should().Be(24);
    }

    #endregion
}