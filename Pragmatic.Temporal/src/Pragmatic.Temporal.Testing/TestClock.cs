using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Testing;

/// <summary>
///     A clock implementation for testing that allows time manipulation.
/// </summary>
public sealed class TestClock : IClock
{
    private readonly object _lock = new();
    private DateTimeOffset _currentTime;

    /// <summary>Creates a TestClock set to the specified time (defaults to 2024-01-15T12:00:00Z).</summary>
    public TestClock(DateTimeOffset? initialTime = null)
    {
        _currentTime = initialTime ?? new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
    }

    #region IClock Implementation

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_lock)
            {
                var time = _currentTime.ToUniversalTime();
                if (AutoAdvance)
                    _currentTime = _currentTime.Add(AutoAdvanceAmount);
                return time;
            }
        }
    }

    /// <inheritdoc />
    public DateTimeOffset Now
    {
        get
        {
            lock (_lock)
            {
                var time = _currentTime;
                if (AutoAdvance)
                    _currentTime = _currentTime.Add(AutoAdvanceAmount);
                return time;
            }
        }
    }

    /// <inheritdoc />
    public DateOnly UtcToday => DateOnly.FromDateTime(GetTimeWithoutAdvance().UtcDateTime);

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(GetTimeWithoutAdvance().DateTime);

    /// <inheritdoc />
    public TimeOnly UtcTimeOfDay => TimeOnly.FromDateTime(GetTimeWithoutAdvance().UtcDateTime);

    /// <inheritdoc />
    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(GetTimeWithoutAdvance().DateTime);

    /// <inheritdoc />
    public TimeProvider GetTimeProvider()
    {
        return new TestTimeProvider(this);
    }

    private DateTimeOffset GetTimeWithoutAdvance()
    {
        lock (_lock)
        {
            return _currentTime;
        }
    }

    #endregion

    #region Time Manipulation

    /// <summary>Sets the clock to a specific time.</summary>
    public void Set(DateTimeOffset time)
    {
        lock (_lock)
        {
            _currentTime = time;
        }
    }

    /// <summary>Advances the clock by the specified duration.</summary>
    public void Advance(TimeSpan duration)
    {
        lock (_lock)
        {
            _currentTime = _currentTime.Add(duration);
        }
    }

    /// <summary>Advances the clock by the specified duration.</summary>
    public void Advance(Duration duration)
    {
        Advance(duration.ToTimeSpan());
    }

    /// <summary>Advances the clock by the specified number of days.</summary>
    public void AdvanceDays(int days)
    {
        Advance(TimeSpan.FromDays(days));
    }

    /// <summary>Advances the clock by the specified number of hours.</summary>
    public void AdvanceHours(int hours)
    {
        Advance(TimeSpan.FromHours(hours));
    }

    /// <summary>Advances the clock by the specified number of minutes.</summary>
    public void AdvanceMinutes(int minutes)
    {
        Advance(TimeSpan.FromMinutes(minutes));
    }

    /// <summary>Advances the clock by the specified number of seconds.</summary>
    public void AdvanceSeconds(int seconds)
    {
        Advance(TimeSpan.FromSeconds(seconds));
    }

    #endregion

    #region Auto-Advance

    /// <summary>
    ///     When enabled, automatically advances time on each read.
    ///     Prevents flaky tests when testing async code that depends on time progression.
    ///     Default: false
    /// </summary>
    public bool AutoAdvance { get; set; }

    /// <summary>
    ///     How much to advance on each read when AutoAdvance is enabled.
    ///     Default: 1 millisecond
    /// </summary>
    public TimeSpan AutoAdvanceAmount { get; set; } = TimeSpan.FromMilliseconds(1);

    #endregion

    #region Fluent API

    /// <summary>Sets to a specific date at midnight UTC.</summary>
    public TestClock SetDate(int year, int month, int day)
    {
        Set(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));
        return this;
    }

    /// <summary>Sets to a specific date and time UTC.</summary>
    public TestClock SetDateTime(int year, int month, int day, int hour, int minute, int second = 0)
    {
        Set(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));
        return this;
    }

    /// <summary>Sets to a specific date and time in a timezone.</summary>
    public TestClock SetDateTime(int year, int month, int day, int hour, int minute, TimeZoneInfo zone)
    {
        var local = new DateTime(year, month, day, hour, minute, 0);
        var offset = zone.GetUtcOffset(local);
        Set(new DateTimeOffset(local, offset));
        return this;
    }

    /// <summary>Enables auto-advance.</summary>
    public TestClock WithAutoAdvance(TimeSpan? amount = null)
    {
        AutoAdvance = true;
        if (amount.HasValue)
            AutoAdvanceAmount = amount.Value;
        return this;
    }

    #endregion

    #region DST Test Helpers

    /// <summary>
    ///     Sets clock to just before DST spring forward (02:00 → 03:00) in Europe/Rome.
    ///     2024-03-31 01:59:59 +01:00
    /// </summary>
    public TestClock SetBeforeRomeSpringForward(int year = 2024)
    {
        // Last Sunday of March
        var lastDayOfMarch = new DateTime(year, 3, 31);
        while (lastDayOfMarch.DayOfWeek != DayOfWeek.Sunday)
            lastDayOfMarch = lastDayOfMarch.AddDays(-1);

        Set(new DateTimeOffset(lastDayOfMarch.Year, lastDayOfMarch.Month, lastDayOfMarch.Day,
            1, 59, 59, TimeSpan.FromHours(1)));
        return this;
    }

    /// <summary>
    ///     Sets clock to just before DST fall back (03:00 → 02:00) in Europe/Rome.
    ///     2024-10-27 02:59:59 +02:00
    /// </summary>
    public TestClock SetBeforeRomeFallBack(int year = 2024)
    {
        // Last Sunday of October
        var lastDayOfOctober = new DateTime(year, 10, 31);
        while (lastDayOfOctober.DayOfWeek != DayOfWeek.Sunday)
            lastDayOfOctober = lastDayOfOctober.AddDays(-1);

        Set(new DateTimeOffset(lastDayOfOctober.Year, lastDayOfOctober.Month, lastDayOfOctober.Day,
            2, 59, 59, TimeSpan.FromHours(2)));
        return this;
    }

    /// <summary>
    ///     Sets clock to just before DST spring forward in US Eastern Time.
    ///     Second Sunday of March at 01:59:59 -05:00
    /// </summary>
    public TestClock SetBeforeUsEasternSpringForward(int year = 2024)
    {
        // Second Sunday of March
        var firstOfMarch = new DateTime(year, 3, 1);
        var firstSunday = firstOfMarch.AddDays((7 - (int)firstOfMarch.DayOfWeek) % 7);
        var secondSunday = firstSunday.AddDays(7);

        Set(new DateTimeOffset(secondSunday.Year, secondSunday.Month, secondSunday.Day,
            1, 59, 59, TimeSpan.FromHours(-5)));
        return this;
    }

    #endregion

    #region Static Factories

    /// <summary>Creates a TestClock set to now (UTC).</summary>
    public static TestClock AtNow()
    {
        return new TestClock(DateTimeOffset.UtcNow);
    }

    /// <summary>Creates a TestClock set to a specific date at noon UTC.</summary>
    public static TestClock AtNoon(int year, int month, int day)
    {
        return new TestClock(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Creates a TestClock set to midnight UTC on a specific date.</summary>
    public static TestClock AtMidnight(int year, int month, int day)
    {
        return new TestClock(new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));
    }

    #endregion
}

