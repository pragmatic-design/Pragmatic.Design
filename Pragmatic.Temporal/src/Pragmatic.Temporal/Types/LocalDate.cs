using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Temporal.Types;

/// <summary>
///     Represents a date without timezone information (wall clock date).
///     Use this when the date is meaningful regardless of timezone,
///     such as birthdays, holidays, or deadlines.
///     <para>
///         Unlike <see cref="DateOnly" />, this type is designed to work
///         with the Pragmatic.Temporal ecosystem for DST-safe operations.
///     </para>
/// </summary>
public readonly partial struct LocalDate : IEquatable<LocalDate>, IComparable<LocalDate>, IComparable,
    IParsable<LocalDate>, IFormattable
{
    private readonly DateOnly _value;

    /// <summary>Creates a new <see cref="LocalDate" /> from year, month, and day.</summary>
    public LocalDate(int year, int month, int day)
    {
        _value = new DateOnly(year, month, day);
    }

    /// <summary>Creates a new <see cref="LocalDate" /> from a <see cref="DateOnly" />.</summary>
    public LocalDate(DateOnly dateOnly)
    {
        _value = dateOnly;
    }

    #region Properties

    /// <summary>Gets the year component.</summary>
    public int Year => _value.Year;

    /// <summary>Gets the month component (1-12).</summary>
    public int Month => _value.Month;

    /// <summary>Gets the day component (1-31).</summary>
    public int Day => _value.Day;

    /// <summary>Gets the day of week.</summary>
    public DayOfWeek DayOfWeek => _value.DayOfWeek;

    /// <summary>Gets the day of year (1-366).</summary>
    public int DayOfYear => _value.DayOfYear;

    #endregion

    #region Factory Methods

    /// <summary>Gets the minimum possible date value.</summary>
    public static LocalDate MinValue { get; } = new(DateOnly.MinValue);

    /// <summary>Gets the maximum possible date value.</summary>
    public static LocalDate MaxValue { get; } = new(DateOnly.MaxValue);

    /// <summary>Creates a LocalDate from a DateTime (uses the Date part only).</summary>
    public static LocalDate FromDateTime(DateTime dateTime)
    {
        return new LocalDate(DateOnly.FromDateTime(dateTime));
    }

    /// <summary>
    ///     Creates a LocalDate from the wall-clock date of the value's own offset —
    ///     no timezone conversion is performed. To get "the date of this instant in a
    ///     specific zone", use <c>DateTimeOffsetExtensions.ToLocalDate(zone)</c> instead.
    /// </summary>
    public static LocalDate FromDateTimeOffset(DateTimeOffset dateTimeOffset)
    {
        return new LocalDate(DateOnly.FromDateTime(dateTimeOffset.DateTime));
    }

    #endregion

    #region Arithmetic

    /// <summary>Returns a new LocalDate with the specified number of days added.</summary>
    public LocalDate AddDays(int days)
    {
        return new LocalDate(_value.AddDays(days));
    }

    /// <summary>Returns a new LocalDate with the specified number of months added.</summary>
    public LocalDate AddMonths(int months)
    {
        return new LocalDate(_value.AddMonths(months));
    }

    /// <summary>Returns a new LocalDate with the specified number of years added.</summary>
    public LocalDate AddYears(int years)
    {
        return new LocalDate(_value.AddYears(years));
    }

    /// <summary>Returns the number of days between this date and another date.</summary>
    public int DaysBetween(LocalDate other)
    {
        return _value.DayNumber - other._value.DayNumber;
    }

    #endregion

    #region Navigation

    /// <summary>Returns the first day of the current month.</summary>
    public LocalDate StartOfMonth()
    {
        return new LocalDate(Year, Month, 1);
    }

    /// <summary>Returns the last day of the current month.</summary>
    public LocalDate EndOfMonth()
    {
        return new LocalDate(Year, Month, DateTime.DaysInMonth(Year, Month));
    }

    /// <summary>Returns the first day of the current year.</summary>
    public LocalDate StartOfYear()
    {
        return new LocalDate(Year, 1, 1);
    }

    /// <summary>Returns the last day of the current year.</summary>
    public LocalDate EndOfYear()
    {
        return new LocalDate(Year, 12, 31);
    }

    /// <summary>Returns the first day of the week containing this date.</summary>
    public LocalDate StartOfWeek(DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
    {
        var diff = (7 + (DayOfWeek - firstDayOfWeek)) % 7;
        return AddDays(-diff);
    }

    /// <summary>Returns the last day of the week containing this date.</summary>
    public LocalDate EndOfWeek(DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
    {
        return StartOfWeek(firstDayOfWeek).AddDays(6);
    }

    /// <summary>Returns the quarter (1-4) for this date.</summary>
    public int Quarter => (Month - 1) / 3 + 1;

    /// <summary>Returns the first day of the current quarter.</summary>
    public LocalDate StartOfQuarter()
    {
        var quarterStartMonth = (Quarter - 1) * 3 + 1;
        return new LocalDate(Year, quarterStartMonth, 1);
    }

    /// <summary>Returns the last day of the current quarter.</summary>
    public LocalDate EndOfQuarter()
    {
        var quarterEndMonth = Quarter * 3;
        return new LocalDate(Year, quarterEndMonth, DateTime.DaysInMonth(Year, quarterEndMonth));
    }

    #endregion

    #region Checks

    /// <summary>Returns true if this date is a weekend (Saturday or Sunday).</summary>
    public bool IsWeekend => DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>Returns true if this date is a weekday (Monday through Friday).</summary>
    public bool IsWeekday => !IsWeekend;

    /// <summary>Returns true if this year is a leap year.</summary>
    public bool IsLeapYear => DateTime.IsLeapYear(Year);

    #endregion

    #region Conversion

    /// <summary>Converts to a <see cref="DateOnly" />.</summary>
    public DateOnly ToDateOnly()
    {
        return _value;
    }

    /// <summary>Converts to a <see cref="DateTime" /> at midnight.</summary>
    public DateTime ToDateTime()
    {
        return _value.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>Converts to a <see cref="DateTime" /> at the specified time.</summary>
    public DateTime ToDateTime(LocalTime time)
    {
        return _value.ToDateTime(time.ToTimeOnly());
    }

    /// <summary>Combines with a <see cref="LocalTime" /> to create a <see cref="LocalDateTime" />.</summary>
    public LocalDateTime At(LocalTime time)
    {
        return new LocalDateTime(this, time);
    }

    /// <summary>Creates a LocalDateTime at midnight.</summary>
    public LocalDateTime AtMidnight()
    {
        return new LocalDateTime(this, LocalTime.Midnight);
    }

    /// <summary>Creates a LocalDateTime at noon.</summary>
    public LocalDateTime AtNoon()
    {
        return new LocalDateTime(this, LocalTime.Noon);
    }

    #endregion

    #region Equality & Comparison

    public bool Equals(LocalDate other)
    {
        return _value == other._value;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is LocalDate other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public int CompareTo(LocalDate other)
    {
        return _value.CompareTo(other._value);
    }

    public int CompareTo(object? obj)
    {
        return obj is LocalDate other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(LocalDate)}", nameof(obj));
    }

    #endregion
}