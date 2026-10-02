using System.Diagnostics.CodeAnalysis;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal.Testing;

/// <summary>
///     A pre-configured TemporalContext for testing with easy time manipulation.
/// </summary>
public sealed class TestTemporalContext : TemporalContext
{
    /// <summary>Creates a TestTemporalContext with the specified configuration.</summary>
    [SetsRequiredMembers]
    public TestTemporalContext(
        TimeZoneInfo? clientTimeZone = null,
        TimeZoneInfo? businessTimeZone = null,
        DateTimeOffset? now = null)
    {
        TestClock = new TestClock(now);

        Clock = TestClock;
        ClientTimeZone = clientTimeZone ?? TimeZoneInfo.Utc;
        BusinessTimeZone = businessTimeZone ?? TimeZoneInfo.Utc;
    }

    // Required constructor to satisfy base class
    [SetsRequiredMembers]
    private TestTemporalContext(TestClock clock, TimeZoneInfo client, TimeZoneInfo business)
    {
        TestClock = clock;
        Clock = clock;
        ClientTimeZone = client;
        BusinessTimeZone = business;
    }

    /// <summary>Access to the underlying TestClock for manipulation.</summary>
    public TestClock TestClock { get; }

    #region Time Manipulation Shortcuts

    /// <summary>Sets the clock to a specific time.</summary>
    public void SetTime(DateTimeOffset time)
    {
        TestClock.Set(time);
    }

    /// <summary>Advances the clock by the specified duration.</summary>
    public void Advance(TimeSpan duration)
    {
        TestClock.Advance(duration);
    }

    /// <summary>Advances the clock by the specified number of days.</summary>
    public void AdvanceDays(int days)
    {
        TestClock.AdvanceDays(days);
    }

    /// <summary>Sets the clock to a specific date and time.</summary>
    public void SetDateTime(int year, int month, int day, int hour, int minute, int second = 0)
    {
        TestClock.SetDateTime(year, month, day, hour, minute, second);
    }

    #endregion

    #region Static Factories

    /// <summary>Creates a UTC context.</summary>
    public static TestTemporalContext Utc(DateTimeOffset? now = null)
    {
        return new TestTemporalContext(TimeZoneInfo.Utc, TimeZoneInfo.Utc, now);
    }

    /// <summary>Creates a context for a specific timezone.</summary>
    public static TestTemporalContext ForTimezone(string timezoneId, DateTimeOffset? now = null)
    {
        var zone = TimeZoneResolver.GetTimeZone(timezoneId);
        return new TestTemporalContext(zone, zone, now);
    }

    /// <summary>Creates a context for a specific timezone.</summary>
    public static TestTemporalContext ForTimezone(TimeZoneInfo zone, DateTimeOffset? now = null)
    {
        return new TestTemporalContext(zone, zone, now);
    }

    /// <summary>Creates a context for Europe/Rome timezone.</summary>
    public static TestTemporalContext ForRome(DateTimeOffset? now = null)
    {
        return ForTimezone("Europe/Rome", now);
    }

    /// <summary>Creates a context for America/New_York timezone.</summary>
    public static TestTemporalContext ForNewYork(DateTimeOffset? now = null)
    {
        return ForTimezone("America/New_York", now);
    }

    /// <summary>Creates a context for America/Los_Angeles timezone.</summary>
    public static TestTemporalContext ForLosAngeles(DateTimeOffset? now = null)
    {
        return ForTimezone("America/Los_Angeles", now);
    }

    /// <summary>Creates a context for Europe/London timezone.</summary>
    public static TestTemporalContext ForLondon(DateTimeOffset? now = null)
    {
        return ForTimezone("Europe/London", now);
    }

    /// <summary>Creates a context with different client and business timezones.</summary>
    public static TestTemporalContext WithTimezones(
        string clientTimezoneId,
        string businessTimezoneId,
        DateTimeOffset? now = null)
    {
        var clientZone = TimeZoneResolver.GetTimeZone(clientTimezoneId);
        var businessZone = TimeZoneResolver.GetTimeZone(businessTimezoneId);
        return new TestTemporalContext(clientZone, businessZone, now);
    }

    #endregion
}