using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Temporal.Types;

/// <summary>
///     Immutable duration value type representing elapsed physical time.
///     Unlike calendar arithmetic, Duration always represents exact elapsed time.
///     <para>
///         <c>Duration.FromDays(1)</c> is ALWAYS 24 hours.
///         <c>ZonedDateTime.AddDays(1)</c> means "same time tomorrow" (may be 23h or 25h due to DST).
///     </para>
/// </summary>
public readonly partial struct Duration : IEquatable<Duration>, IComparable<Duration>, IComparable,
    IParsable<Duration>, IFormattable
{
    private readonly TimeSpan _value;

    private Duration(TimeSpan value)
    {
        _value = value;
    }

    #region Common Durations

    /// <summary>A duration of zero.</summary>
    public static Duration Zero { get; } = new(TimeSpan.Zero);

    /// <summary>A duration of one day (24 hours).</summary>
    public static Duration OneDay { get; } = new(TimeSpan.FromDays(1));

    /// <summary>A duration of one hour.</summary>
    public static Duration OneHour { get; } = new(TimeSpan.FromHours(1));

    /// <summary>A duration of one minute.</summary>
    public static Duration OneMinute { get; } = new(TimeSpan.FromMinutes(1));

    /// <summary>A duration of one second.</summary>
    public static Duration OneSecond { get; } = new(TimeSpan.FromSeconds(1));

    /// <summary>A duration of one millisecond.</summary>
    public static Duration OneMillisecond { get; } = new(TimeSpan.FromMilliseconds(1));

    #endregion

    #region Factory Methods

    /// <summary>Creates a duration from the specified number of days.</summary>
    public static Duration FromDays(double days)
    {
        return new Duration(TimeSpan.FromDays(days));
    }

    /// <summary>Creates a duration from the specified number of hours.</summary>
    public static Duration FromHours(double hours)
    {
        return new Duration(TimeSpan.FromHours(hours));
    }

    /// <summary>Creates a duration from the specified number of minutes.</summary>
    public static Duration FromMinutes(double minutes)
    {
        return new Duration(TimeSpan.FromMinutes(minutes));
    }

    /// <summary>Creates a duration from the specified number of seconds.</summary>
    public static Duration FromSeconds(double seconds)
    {
        return new Duration(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>Creates a duration from the specified number of milliseconds.</summary>
    public static Duration FromMilliseconds(double milliseconds)
    {
        return new Duration(TimeSpan.FromMilliseconds(milliseconds));
    }

    /// <summary>Creates a duration from the specified number of ticks.</summary>
    public static Duration FromTicks(long ticks)
    {
        return new Duration(TimeSpan.FromTicks(ticks));
    }

    /// <summary>Creates a duration from a <see cref="TimeSpan" />.</summary>
    public static Duration FromTimeSpan(TimeSpan timeSpan)
    {
        return new Duration(timeSpan);
    }

    #endregion

    #region Properties

    /// <summary>Gets the total number of days in this duration.</summary>
    public double TotalDays => _value.TotalDays;

    /// <summary>Gets the total number of hours in this duration.</summary>
    public double TotalHours => _value.TotalHours;

    /// <summary>Gets the total number of minutes in this duration.</summary>
    public double TotalMinutes => _value.TotalMinutes;

    /// <summary>Gets the total number of seconds in this duration.</summary>
    public double TotalSeconds => _value.TotalSeconds;

    /// <summary>Gets the total number of milliseconds in this duration.</summary>
    public double TotalMilliseconds => _value.TotalMilliseconds;

    /// <summary>Gets the number of ticks in this duration.</summary>
    public long Ticks => _value.Ticks;

    /// <summary>Returns true if this duration is zero.</summary>
    public bool IsZero => _value == TimeSpan.Zero;

    /// <summary>Returns true if this duration is negative.</summary>
    public bool IsNegative => _value < TimeSpan.Zero;

    /// <summary>Returns true if this duration is positive (greater than zero).</summary>
    public bool IsPositive => _value > TimeSpan.Zero;

    #endregion

    #region Conversion

    /// <summary>Converts this duration to a <see cref="TimeSpan" />.</summary>
    public TimeSpan ToTimeSpan()
    {
        return _value;
    }

    /// <summary>Returns the absolute value of this duration.</summary>
    public Duration Abs()
    {
        return new Duration(_value.Duration());
    }

    /// <summary>Returns the negation of this duration.</summary>
    public Duration Negate()
    {
        return new Duration(-_value);
    }

    #endregion

    #region Equality & Comparison

    public bool Equals(Duration other)
    {
        return _value == other._value;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is Duration other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public int CompareTo(Duration other)
    {
        return _value.CompareTo(other._value);
    }

    public int CompareTo(object? obj)
    {
        return obj is Duration other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(Duration)}", nameof(obj));
    }

    #endregion
}