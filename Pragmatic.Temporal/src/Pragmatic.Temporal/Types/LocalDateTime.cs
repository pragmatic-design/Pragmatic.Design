using System.Diagnostics.CodeAnalysis;
using Pragmatic.Temporal.Internal;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal.Types;

/// <summary>
///     Represents a date and time without timezone information (wall clock datetime).
///     Use this when the datetime is meaningful regardless of timezone,
///     such as recurring meetings, store hours, or "2024-01-15 at 10:00 AM".
///     <para>
///         ⚠️ Do NOT use this for absolute instants in time. Use <see cref="DateTimeOffset" />
///         or <see cref="ZonedDateTime" /> for events that happened at a specific moment.
///     </para>
/// </summary>
public readonly partial struct LocalDateTime : IEquatable<LocalDateTime>, IComparable<LocalDateTime>, IComparable,
    IParsable<LocalDateTime>, IFormattable
{
    private readonly DateTime _value;

    /// <summary>Creates a new <see cref="LocalDateTime" /> from a date and time.</summary>
    public LocalDateTime(LocalDate date, LocalTime time)
    {
        _value = date.ToDateTime(time);
    }

    /// <summary>Creates a new <see cref="LocalDateTime" /> from components.</summary>
    public LocalDateTime(int year, int month, int day, int hour, int minute, int second = 0)
    {
        _value = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
    }

    /// <summary>Creates a new <see cref="LocalDateTime" /> from a <see cref="DateTime" />.</summary>
    /// <remarks>The Kind of the DateTime is ignored; it's treated as local wall clock time.</remarks>
    public LocalDateTime(DateTime dateTime)
    {
        _value = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
    }

    #region Properties

    /// <summary>Gets the date part.</summary>
    public LocalDate Date => new(DateOnly.FromDateTime(_value));

    /// <summary>Gets the time part.</summary>
    public LocalTime Time => new(TimeOnly.FromDateTime(_value));

    /// <summary>Gets the year component.</summary>
    public int Year => _value.Year;

    /// <summary>Gets the month component (1-12).</summary>
    public int Month => _value.Month;

    /// <summary>Gets the day component (1-31).</summary>
    public int Day => _value.Day;

    /// <summary>Gets the hour component (0-23).</summary>
    public int Hour => _value.Hour;

    /// <summary>Gets the minute component (0-59).</summary>
    public int Minute => _value.Minute;

    /// <summary>Gets the second component (0-59).</summary>
    public int Second => _value.Second;

    /// <summary>Gets the day of week.</summary>
    public DayOfWeek DayOfWeek => _value.DayOfWeek;

    /// <summary>Gets the day of year (1-366).</summary>
    public int DayOfYear => _value.DayOfYear;

    #endregion

    #region Factory Methods

    /// <summary>Gets the minimum possible datetime value.</summary>
    public static LocalDateTime MinValue { get; } = new(DateTime.MinValue);

    /// <summary>Gets the maximum possible datetime value.</summary>
    public static LocalDateTime MaxValue { get; } = new(DateTime.MaxValue);

    #endregion

    #region Arithmetic

    /// <summary>Returns a new LocalDateTime with the specified duration added.</summary>
    public LocalDateTime Add(Duration duration)
    {
        return new LocalDateTime(_value.Add(duration.ToTimeSpan()));
    }

    /// <summary>Returns a new LocalDateTime with the specified TimeSpan added.</summary>
    public LocalDateTime Add(TimeSpan timeSpan)
    {
        return new LocalDateTime(_value.Add(timeSpan));
    }

    /// <summary>Returns a new LocalDateTime with the specified days added.</summary>
    public LocalDateTime AddDays(int days)
    {
        return new LocalDateTime(_value.AddDays(days));
    }

    /// <summary>Returns a new LocalDateTime with the specified months added.</summary>
    public LocalDateTime AddMonths(int months)
    {
        return new LocalDateTime(_value.AddMonths(months));
    }

    /// <summary>Returns a new LocalDateTime with the specified years added.</summary>
    public LocalDateTime AddYears(int years)
    {
        return new LocalDateTime(_value.AddYears(years));
    }

    /// <summary>Returns a new LocalDateTime with the specified hours added.</summary>
    public LocalDateTime AddHours(int hours)
    {
        return new LocalDateTime(_value.AddHours(hours));
    }

    /// <summary>Returns a new LocalDateTime with the specified minutes added.</summary>
    public LocalDateTime AddMinutes(int minutes)
    {
        return new LocalDateTime(_value.AddMinutes(minutes));
    }

    /// <summary>Returns a new LocalDateTime with the specified seconds added.</summary>
    public LocalDateTime AddSeconds(int seconds)
    {
        return new LocalDateTime(_value.AddSeconds(seconds));
    }

    /// <summary>Returns the duration between this datetime and another.</summary>
    public Duration DurationUntil(LocalDateTime other)
    {
        return Duration.FromTimeSpan(other._value - _value);
    }

    #endregion

    #region Navigation

    /// <summary>Returns the start of the day (midnight).</summary>
    public LocalDateTime StartOfDay()
    {
        return new LocalDateTime(Date, LocalTime.Midnight);
    }

    /// <summary>Returns the end of the day (23:59:59.999).</summary>
    public LocalDateTime EndOfDay()
    {
        return new LocalDateTime(Date, LocalTime.MaxValue);
    }

    /// <summary>Returns the start of the hour.</summary>
    public LocalDateTime StartOfHour()
    {
        return new LocalDateTime(Year, Month, Day, Hour, 0);
    }

    #endregion

    #region Conversion

    /// <summary>Gets the underlying DateTime value (Kind is Unspecified).</summary>
    public DateTime ToDateTime()
    {
        return _value;
    }

    /// <summary>
    ///     Converts to a <see cref="ZonedDateTime" /> in the specified timezone.
    /// </summary>
    /// <param name="zone">The timezone to interpret this local datetime in.</param>
    /// <param name="nonExistentPolicy">How to handle times that don't exist (DST spring forward).</param>
    /// <param name="ambiguousPolicy">How to handle ambiguous times (DST fall back).</param>
    public ZonedDateTime InZone(
        TimeZoneInfo zone,
        NonExistentTimePolicy nonExistentPolicy = NonExistentTimePolicy.ShiftForward,
        AmbiguousTimePolicy ambiguousPolicy = AmbiguousTimePolicy.UseStandardTime)
    {
        return ZonedDateTime.FromLocal(_value, zone, nonExistentPolicy, ambiguousPolicy);
    }

    /// <summary>
    ///     Converts to a <see cref="ZonedDateTime" /> in the specified timezone.
    /// </summary>
    public ZonedDateTime InZone(string timezoneId,
        NonExistentTimePolicy nonExistentPolicy = NonExistentTimePolicy.ShiftForward,
        AmbiguousTimePolicy ambiguousPolicy = AmbiguousTimePolicy.UseStandardTime)
    {
        var zone = TimeZoneResolver.GetTimeZone(timezoneId);
        return InZone(zone, nonExistentPolicy, ambiguousPolicy);
    }

    /// <summary>
    ///     Converts to a <see cref="DateTimeOffset" /> assuming the specified timezone,
    ///     using the default DST policies (shift forward through gaps, standard time
    ///     for ambiguous times) — consistent with <see cref="InZone(TimeZoneInfo, NonExistentTimePolicy, AmbiguousTimePolicy)" />.
    /// </summary>
    public DateTimeOffset ToDateTimeOffset(TimeZoneInfo zone)
    {
        return ToDateTimeOffset(zone, NonExistentTimePolicy.ShiftForward, AmbiguousTimePolicy.UseStandardTime);
    }

    /// <summary>
    ///     Converts to a <see cref="DateTimeOffset" /> assuming the specified timezone,
    ///     with explicit policies for DST gaps and ambiguous times.
    /// </summary>
    /// <param name="zone">The timezone to interpret this local datetime in.</param>
    /// <param name="nonExistentPolicy">How to handle times that don't exist (DST spring forward).</param>
    /// <param name="ambiguousPolicy">How to handle ambiguous times (DST fall back).</param>
    public DateTimeOffset ToDateTimeOffset(
        TimeZoneInfo zone,
        NonExistentTimePolicy nonExistentPolicy,
        AmbiguousTimePolicy ambiguousPolicy)
    {
        var utc = DstHelper.LocalToUtc(_value, zone, nonExistentPolicy, ambiguousPolicy);
        return TimeZoneInfo.ConvertTime(utc, zone);
    }

    /// <summary>Deconstructs into date and time components.</summary>
    public void Deconstruct(out LocalDate date, out LocalTime time)
    {
        date = Date;
        time = Time;
    }

    #endregion

    #region Equality & Comparison

    public bool Equals(LocalDateTime other)
    {
        return _value == other._value;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is LocalDateTime other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public int CompareTo(LocalDateTime other)
    {
        return _value.CompareTo(other._value);
    }

    public int CompareTo(object? obj)
    {
        return obj is LocalDateTime other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(LocalDateTime)}", nameof(obj));
    }

    #endregion
}