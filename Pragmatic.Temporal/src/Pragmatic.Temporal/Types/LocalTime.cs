using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Temporal.Types;

/// <summary>
///     Represents a time of day without timezone information (wall clock time).
///     Use this when the time is meaningful regardless of timezone,
///     such as store opening hours, meeting times, or alarm times.
/// </summary>
public readonly partial struct LocalTime : IEquatable<LocalTime>, IComparable<LocalTime>, IComparable,
    IParsable<LocalTime>, IFormattable
{
    private readonly TimeOnly _value;

    /// <summary>Creates a new <see cref="LocalTime" /> from hour and minute.</summary>
    public LocalTime(int hour, int minute) : this(hour, minute, 0)
    {
    }

    /// <summary>Creates a new <see cref="LocalTime" /> from hour, minute, and second.</summary>
    public LocalTime(int hour, int minute, int second)
    {
        _value = new TimeOnly(hour, minute, second);
    }

    /// <summary>Creates a new <see cref="LocalTime" /> from hour, minute, second, and millisecond.</summary>
    public LocalTime(int hour, int minute, int second, int millisecond)
    {
        _value = new TimeOnly(hour, minute, second, millisecond);
    }

    /// <summary>Creates a new <see cref="LocalTime" /> from a <see cref="TimeOnly" />.</summary>
    public LocalTime(TimeOnly timeOnly)
    {
        _value = timeOnly;
    }

    #region Properties

    /// <summary>Gets the hour component (0-23).</summary>
    public int Hour => _value.Hour;

    /// <summary>Gets the minute component (0-59).</summary>
    public int Minute => _value.Minute;

    /// <summary>Gets the second component (0-59).</summary>
    public int Second => _value.Second;

    /// <summary>Gets the millisecond component (0-999).</summary>
    public int Millisecond => _value.Millisecond;

    /// <summary>Gets the number of ticks representing this time.</summary>
    public long Ticks => _value.Ticks;

    #endregion

    #region Common Times

    /// <summary>Midnight (00:00:00).</summary>
    public static LocalTime Midnight { get; } = new(TimeOnly.MinValue);

    /// <summary>Noon (12:00:00).</summary>
    public static LocalTime Noon { get; } = new(12, 0, 0);

    /// <summary>The minimum time value (00:00:00).</summary>
    public static LocalTime MinValue { get; } = new(TimeOnly.MinValue);

    /// <summary>The maximum time value (23:59:59.9999999).</summary>
    public static LocalTime MaxValue { get; } = new(TimeOnly.MaxValue);

    #endregion

    #region Factory Methods

    /// <summary>Creates a LocalTime from a DateTime (uses the Time part only).</summary>
    public static LocalTime FromDateTime(DateTime dateTime)
    {
        return new LocalTime(TimeOnly.FromDateTime(dateTime));
    }

    /// <summary>Creates a LocalTime from a DateTimeOffset (uses the Time part in the offset's timezone).</summary>
    public static LocalTime FromDateTimeOffset(DateTimeOffset dateTimeOffset)
    {
        return new LocalTime(TimeOnly.FromDateTime(dateTimeOffset.DateTime));
    }

    /// <summary>Creates a LocalTime from ticks.</summary>
    public static LocalTime FromTicks(long ticks)
    {
        return new LocalTime(new TimeOnly(ticks));
    }

    /// <summary>Creates a LocalTime from a TimeSpan.</summary>
    public static LocalTime FromTimeSpan(TimeSpan timeSpan)
    {
        return new LocalTime(TimeOnly.FromTimeSpan(timeSpan));
    }

    #endregion

    #region Arithmetic

    /// <summary>Returns a new LocalTime with the specified duration added.</summary>
    public LocalTime Add(TimeSpan duration)
    {
        return new LocalTime(_value.Add(duration));
    }

    /// <summary>Returns a new LocalTime with the specified duration added.</summary>
    public LocalTime Add(Duration duration)
    {
        return new LocalTime(_value.Add(duration.ToTimeSpan()));
    }

    /// <summary>Returns a new LocalTime with the specified hours added.</summary>
    public LocalTime AddHours(double hours)
    {
        return new LocalTime(_value.Add(TimeSpan.FromHours(hours)));
    }

    /// <summary>Returns a new LocalTime with the specified minutes added.</summary>
    public LocalTime AddMinutes(double minutes)
    {
        return new LocalTime(_value.Add(TimeSpan.FromMinutes(minutes)));
    }

    /// <summary>Returns the duration between this time and another time.</summary>
    public TimeSpan DurationUntil(LocalTime other)
    {
        return other._value >= _value
            ? other._value - _value
            : TimeSpan.FromDays(1) - (_value - other._value);
    }

    #endregion

    #region Checks

    /// <summary>
    ///     Returns true if this time is between the specified start (inclusive) and end (exclusive).
    ///     Supports ranges that wrap around midnight (e.g. 22:00–06:00).
    /// </summary>
    public bool IsBetween(LocalTime start, LocalTime end)
    {
        return _value.IsBetween(start._value, end._value);
    }

    /// <summary>Returns true if this time represents morning (before noon).</summary>
    public bool IsMorning => Hour < 12;

    /// <summary>Returns true if this time represents afternoon (noon to 6 PM).</summary>
    public bool IsAfternoon => Hour is >= 12 and < 18;

    /// <summary>Returns true if this time represents evening (6 PM onwards).</summary>
    public bool IsEvening => Hour >= 18;

    #endregion

    #region Conversion

    /// <summary>Converts to a <see cref="TimeOnly" />.</summary>
    public TimeOnly ToTimeOnly()
    {
        return _value;
    }

    /// <summary>Converts to a <see cref="TimeSpan" />.</summary>
    public TimeSpan ToTimeSpan()
    {
        return _value.ToTimeSpan();
    }

    /// <summary>Combines with a <see cref="LocalDate" /> to create a <see cref="LocalDateTime" />.</summary>
    public LocalDateTime On(LocalDate date)
    {
        return new LocalDateTime(date, this);
    }

    #endregion

    #region Equality & Comparison

    public bool Equals(LocalTime other)
    {
        return _value == other._value;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is LocalTime other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public int CompareTo(LocalTime other)
    {
        return _value.CompareTo(other._value);
    }

    public int CompareTo(object? obj)
    {
        return obj is LocalTime other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(LocalTime)}", nameof(obj));
    }

    #endregion
}