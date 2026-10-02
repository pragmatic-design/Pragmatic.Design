using System.Diagnostics.CodeAnalysis;

namespace Pragmatic.Temporal.Types;

/// <summary>
///     Represents a calendar-based duration in years, months, and days.
///     Unlike <see cref="Duration" /> which represents exact elapsed time,
///     Period represents calendar units that vary in actual length.
/// </summary>
/// <remarks>
///     <para>
///         A Period of "1 month" added to January 31 results in February 28/29,
///         not an error. This is the expected behavior for calendar arithmetic.
///     </para>
///     <para>
///         Period supports ISO 8601 duration format: P1Y2M3D (1 year, 2 months, 3 days).
///     </para>
/// </remarks>
/// <example>
///     <code>
///     var oneMonth = Period.FromMonths(1);
///     LocalDate today = clock.Today;             // clock is the injected IClock
///     var nextMonth = today.Add(oneMonth);       // Jan 31 → Feb 28/29
///
///     var subscription = Period.FromYears(1);
///     var expiryDate = startDate.Add(subscription);
///     </code>
/// </example>
public readonly partial struct Period : IEquatable<Period>, IParsable<Period>, IFormattable
{
    /// <summary>
    ///     Gets the years component.
    /// </summary>
    public int Years { get; }

    /// <summary>
    ///     Gets the months component.
    /// </summary>
    public int Months { get; }

    /// <summary>
    ///     Gets the days component.
    /// </summary>
    public int Days { get; }

    /// <summary>
    ///     Creates a new Period with the specified components.
    /// </summary>
    public Period(int years, int months, int days)
    {
        Years = years;
        Months = months;
        Days = days;
    }

    #region Properties

    /// <summary>
    ///     Gets whether this period is zero (all components are 0).
    /// </summary>
    public bool IsZero => Years == 0 && Months == 0 && Days == 0;

    /// <summary>
    ///     Gets whether this period has only date components (no time).
    ///     Always true for Period (unlike Duration which can have sub-day precision).
    /// </summary>
    public static bool IsDateOnly => true;

    /// <summary>
    ///     Gets whether any component is negative.
    /// </summary>
    public bool IsNegative => Years < 0 || Months < 0 || Days < 0;

    /// <summary>
    ///     Gets the total months (years * 12 + months), ignoring days.
    /// </summary>
    public int TotalMonths => Years * 12 + Months;

    #endregion

    #region Factory Methods

    /// <summary>
    ///     A zero period.
    /// </summary>
    public static Period Zero { get; } = new(0, 0, 0);

    /// <summary>
    ///     Creates a period of the specified number of years.
    /// </summary>
    public static Period FromYears(int years) => new(years, 0, 0);

    /// <summary>
    ///     Creates a period of the specified number of months.
    /// </summary>
    public static Period FromMonths(int months) => new(0, months, 0);

    /// <summary>
    ///     Creates a period of the specified number of days.
    /// </summary>
    public static Period FromDays(int days) => new(0, 0, days);

    /// <summary>
    ///     Creates a period of the specified number of weeks (converted to days).
    /// </summary>
    public static Period FromWeeks(int weeks) => new(0, 0, weeks * 7);

    /// <summary>
    ///     Calculates the period between two dates.
    /// </summary>
    /// <remarks>
    ///     The result represents the calendar difference, with years and months
    ///     calculated first, then remaining days.
    /// </remarks>
    public static Period Between(LocalDate start, LocalDate end)
    {
        if (start > end)
            return Between(end, start).Negate();

        var years = end.Year - start.Year;
        var months = end.Month - start.Month;
        var days = end.Day - start.Day;

        // Adjust for negative days
        if (days < 0)
        {
            months--;
            // Days in the previous month of the end date
            var prevMonth = end.AddMonths(-1);
            days += DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
        }

        // Adjust for negative months
        if (months < 0)
        {
            years--;
            months += 12;
        }

        return new Period(years, months, days);
    }

    #endregion

    #region Arithmetic

    /// <summary>
    ///     Returns a new period with all components negated.
    /// </summary>
    public Period Negate() => new(-Years, -Months, -Days);

    /// <summary>
    ///     Returns a normalized period where months are less than 12.
    /// </summary>
    /// <remarks>
    ///     For example, 0 years, 14 months, 5 days becomes 1 year, 2 months, 5 days.
    ///     Days are not normalized as their length varies by month.
    /// </remarks>
    public Period Normalize()
    {
        var totalMonths = TotalMonths;
        var normalizedYears = totalMonths / 12;
        var normalizedMonths = totalMonths % 12;

        // Handle negative values
        if (normalizedMonths < 0)
        {
            normalizedYears--;
            normalizedMonths += 12;
        }

        return new Period(normalizedYears, normalizedMonths, Days);
    }

    #endregion

    #region Equality

    public bool Equals(Period other)
        => Years == other.Years && Months == other.Months && Days == other.Days;

    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is Period other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Years, Months, Days);

    #endregion

    /// <summary>
    ///     Deconstructs the period into its components.
    /// </summary>
    public void Deconstruct(out int years, out int months, out int days)
    {
        years = Years;
        months = Months;
        days = Days;
    }
}
