namespace Pragmatic.Temporal.Types;

/// <summary>
///     Factory methods for <see cref="DateRange" />.
/// </summary>
public readonly partial struct DateRange
{
    #region Factory Methods

    /// <summary>
    ///     An empty date range: contains no dates, enumerates nothing, <c>Days == 0</c>,
    ///     and never overlaps. Internally represented as [MinValue, MinValue]; the
    ///     <c>default</c> value of the struct is also empty.
    /// </summary>
    public static DateRange Empty { get; } = new(LocalDate.MinValue, LocalDate.MinValue);

    /// <summary>
    ///     Creates a range for a single day.
    /// </summary>
    public static DateRange SingleDay(LocalDate date) => new(date, date);

    /// <summary>
    ///     Creates a range for the week containing the specified date.
    /// </summary>
    /// <param name="date">A date within the desired week.</param>
    /// <param name="firstDayOfWeek">The first day of the week (default: Monday).</param>
    public static DateRange Week(LocalDate date, DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
    {
        var start = date.StartOfWeek(firstDayOfWeek);
        var end = date.EndOfWeek(firstDayOfWeek);
        return new DateRange(start, end);
    }


    /// <summary>
    ///     Creates a range for the week containing the specified "today" date.
    ///     Use this overload for testability.
    /// </summary>
    public static DateRange ThisWeek(LocalDate today, DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
        => Week(today, firstDayOfWeek);

    /// <summary>
    ///     Creates a range for the month containing the specified date.
    /// </summary>
    public static DateRange Month(LocalDate date)
    {
        var start = date.StartOfMonth();
        var end = date.EndOfMonth();
        return new DateRange(start, end);
    }

    /// <summary>
    ///     Creates a range for a specific month.
    /// </summary>
    public static DateRange Month(int year, int month)
    {
        var start = new LocalDate(year, month, 1);
        var end = start.EndOfMonth();
        return new DateRange(start, end);
    }


    /// <summary>
    ///     Creates a range for the month containing the specified "today" date.
    ///     Use this overload for testability.
    /// </summary>
    public static DateRange ThisMonth(LocalDate today) => Month(today);

    /// <summary>
    ///     Creates a range for the quarter containing the specified date.
    /// </summary>
    public static DateRange Quarter(LocalDate date)
    {
        var start = date.StartOfQuarter();
        var end = date.EndOfQuarter();
        return new DateRange(start, end);
    }

    /// <summary>
    ///     Creates a range for a specific quarter.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="quarter">The quarter (1-4).</param>
    public static DateRange Quarter(int year, int quarter)
    {
        if (quarter is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(quarter), "Quarter must be between 1 and 4.");

        var startMonth = (quarter - 1) * 3 + 1;
        var start = new LocalDate(year, startMonth, 1);
        var end = start.AddMonths(2).EndOfMonth();
        return new DateRange(start, end);
    }


    /// <summary>
    ///     Creates a range for the quarter containing the specified "today" date.
    ///     Use this overload for testability.
    /// </summary>
    public static DateRange ThisQuarter(LocalDate today) => Quarter(today);

    /// <summary>
    ///     Creates a range for the year containing the specified date.
    /// </summary>
    public static DateRange Year(LocalDate date)
    {
        var start = date.StartOfYear();
        var end = date.EndOfYear();
        return new DateRange(start, end);
    }

    /// <summary>
    ///     Creates a range for a specific year.
    /// </summary>
    public static DateRange Year(int year)
    {
        var start = new LocalDate(year, 1, 1);
        var end = new LocalDate(year, 12, 31);
        return new DateRange(start, end);
    }


    /// <summary>
    ///     Creates a range for the year containing the specified "today" date.
    ///     Use this overload for testability.
    /// </summary>
    public static DateRange ThisYear(LocalDate today) => Year(today);


    /// <summary>
    ///     Creates a range for the last N days ending on the specified date.
    /// </summary>
    /// <param name="endDate">The end date (inclusive).</param>
    /// <param name="days">Number of days (including end date).</param>
    public static DateRange LastDays(LocalDate endDate, int days)
    {
        if (days < 1)
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be at least 1.");

        var start = endDate.AddDays(-(days - 1));
        return new DateRange(start, endDate);
    }


    /// <summary>
    ///     Creates a range for the next N days starting from the specified date.
    /// </summary>
    /// <param name="startDate">The start date (inclusive).</param>
    /// <param name="days">Number of days (including start date).</param>
    public static DateRange NextDays(LocalDate startDate, int days)
    {
        if (days < 1)
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be at least 1.");

        var end = startDate.AddDays(days - 1);
        return new DateRange(startDate, end);
    }

    /// <summary>
    ///     Creates a range between two dates, automatically ordering them.
    /// </summary>
    /// <remarks>
    ///     Unlike the constructor, this method accepts dates in any order.
    /// </remarks>
    public static DateRange Between(LocalDate date1, LocalDate date2)
    {
        return date1 <= date2
            ? new DateRange(date1, date2)
            : new DateRange(date2, date1);
    }

    #endregion
}
