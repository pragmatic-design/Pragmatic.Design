using Pragmatic.Temporal.Holidays;
using Pragmatic.Temporal.Types;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Temporal.Calculator;

/// <summary>
///     Default implementation of <see cref="ITemporalCalculator" />.
///     Uses O(1) mathematical formulas for business day calculations instead of iterative loops.
/// </summary>
public sealed class TemporalCalculator : ITemporalCalculator
{
    private readonly IHolidayProvider _holidayProvider;

    /// <summary>
    ///     Creates a new TemporalCalculator with the specified holiday provider.
    /// </summary>
    public TemporalCalculator(IHolidayProvider holidayProvider)
    {
        ThrowIfNull(holidayProvider);
        _holidayProvider = holidayProvider;
    }

    /// <summary>
    ///     Creates a new TemporalCalculator with no holidays.
    /// </summary>
    public TemporalCalculator() : this(NoHolidaysProvider.Instance)
    {
    }

    #region Business Days

    /// <inheritdoc />
    public LocalDate AddBusinessDays(LocalDate from, int days)
    {
        if (days == 0)
            return from;

        var direction = days > 0 ? 1 : -1;
        var remaining = Math.Abs(days);

        // Optimization: skip full weeks (5 business days = 7 calendar days)
        if (remaining >= 5)
        {
            var fullWeeks = remaining / 5;
            from = from.AddDays(fullWeeks * 7 * direction);
            remaining %= 5;
        }

        // Handle remaining days (max 4 iterations)
        var current = from;
        while (remaining > 0)
        {
            current = current.AddDays(direction);
            if (!current.IsWeekend)
                remaining--;
        }

        return current;
    }

    /// <inheritdoc />
    public LocalDate AddBusinessDays(LocalDate from, int days, string countryCode)
    {
        if (days == 0)
            return from;

        var direction = days > 0 ? 1 : -1;
        var remaining = Math.Abs(days);

        // Optimization: skip full weeks, then adjust for holidays
        if (remaining >= 5)
        {
            var fullWeeks = remaining / 5;
            var targetDate = from.AddDays(fullWeeks * 7 * direction);

            // Count holidays in the skipped range that fall on weekdays
            var holidaysInRange = CountWeekdayHolidaysInRange(
                direction > 0 ? from : targetDate,
                direction > 0 ? targetDate : from,
                countryCode);

            remaining = (remaining % 5) + holidaysInRange;
            from = targetDate;
        }

        // Handle remaining days
        var current = from;
        while (remaining > 0)
        {
            current = current.AddDays(direction);
            if (!current.IsWeekend && !_holidayProvider.IsHoliday(current, countryCode))
                remaining--;
        }

        return current;
    }

    /// <inheritdoc />
    public LocalDate AddBusinessDays(LocalDate from, int days, IEnumerable<LocalDate> holidays)
    {
        if (days == 0)
            return from;

        var holidaySet = holidays.ToHashSet();
        var direction = days > 0 ? 1 : -1;
        var remaining = Math.Abs(days);

        // Optimization: skip full weeks, then adjust for holidays
        if (remaining >= 5)
        {
            var fullWeeks = remaining / 5;
            var targetDate = from.AddDays(fullWeeks * 7 * direction);

            // Count holidays in the skipped range that fall on weekdays
            var rangeStart = direction > 0 ? from : targetDate;
            var rangeEnd = direction > 0 ? targetDate : from;
            var holidaysInRange = holidaySet.Count(h =>
                h > rangeStart && h <= rangeEnd && !h.IsWeekend);

            remaining = (remaining % 5) + holidaysInRange;
            from = targetDate;
        }

        // Handle remaining days
        var current = from;
        while (remaining > 0)
        {
            current = current.AddDays(direction);
            if (!current.IsWeekend && !holidaySet.Contains(current))
                remaining--;
        }

        return current;
    }

    /// <inheritdoc />
    public int CountBusinessDays(LocalDate from, LocalDate to)
    {
        if (from >= to)
            return 0;

        // O(1) calculation using mathematical formula
        var totalDays = to.DaysBetween(from);
        var fullWeeks = totalDays / 7;
        var remainder = totalDays % 7;

        // Full weeks contribute 5 business days each
        var businessDays = fullWeeks * 5;

        // For remaining days, count non-weekend days
        // Using lookup approach for O(1)
        businessDays += CountWeekdaysInRemainder(from.DayOfWeek, remainder);

        return businessDays;
    }

    /// <inheritdoc />
    public int CountBusinessDays(LocalDate from, LocalDate to, string countryCode)
    {
        if (from >= to)
            return 0;

        // Start with weekend-aware count
        var businessDays = CountBusinessDays(from, to);

        // Subtract holidays that fall on weekdays
        businessDays -= CountWeekdayHolidaysInRange(from, to, countryCode);

        return Math.Max(0, businessDays);
    }

    /// <summary>
    ///     Counts weekdays in the remainder using O(1) lookup.
    ///     Counts days in range [startDay, startDay + remainderDays), including startDay.
    /// </summary>
    private static int CountWeekdaysInRemainder(DayOfWeek startDay, int remainderDays)
    {
        if (remainderDays == 0)
            return 0;

        // Convert to 0=Monday, 6=Sunday for easier calculation
        var startIndex = startDay switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => 0
        };

        var weekdays = 0;
        // Count weekdays from startDay for remainderDays days (range [start, start+remainder))
        for (var i = 0; i < remainderDays; i++)
        {
            var dayIndex = (startIndex + i) % 7;
            if (dayIndex < 5) // Monday(0) to Friday(4)
                weekdays++;
        }

        return weekdays;
    }

    /// <summary>
    ///     Counts holidays that fall on weekdays in the given range.
    /// </summary>
    private int CountWeekdayHolidaysInRange(LocalDate from, LocalDate to, string countryCode)
    {
        var count = 0;

        // Process each year in the range
        for (var year = from.Year; year <= to.Year; year++)
        {
            // Dedup by date: a provider may return multiple Holiday records (different
            // Name/Type) for the same calendar date, but business-day math must treat
            // "this date is a holiday" as set membership, not a per-record count.
            var holidayDates = _holidayProvider.GetHolidays(year, countryCode)
                .Select(h => h.Date)
                .Distinct();
            // [from, to), the interval the weekdays were counted on.
            foreach (var date in holidayDates)
                if (date >= from && date < to && !date.IsWeekend)
                    count++;
        }

        return count;
    }

    #endregion

    #region Checks

    /// <inheritdoc />
    public bool IsBusinessDay(LocalDate date)
    {
        return !date.IsWeekend;
    }

    /// <inheritdoc />
    public bool IsBusinessDay(LocalDate date, string countryCode)
    {
        return !date.IsWeekend && !_holidayProvider.IsHoliday(date, countryCode);
    }

    /// <inheritdoc />
    public bool IsHoliday(LocalDate date, string countryCode)
    {
        return _holidayProvider.IsHoliday(date, countryCode);
    }

    /// <inheritdoc />
    public bool IsWeekend(LocalDate date)
    {
        return date.IsWeekend;
    }

    #endregion

    #region Navigation

    /// <inheritdoc />
    public LocalDate NextBusinessDay(LocalDate from)
    {
        var next = from.AddDays(1);
        // Max 2 iterations (Saturday -> Monday)
        while (next.IsWeekend)
            next = next.AddDays(1);
        return next;
    }

    /// <inheritdoc />
    public LocalDate NextBusinessDay(LocalDate from, string countryCode)
    {
        var next = from.AddDays(1);
        while (next.IsWeekend || _holidayProvider.IsHoliday(next, countryCode))
            next = next.AddDays(1);
        return next;
    }

    /// <inheritdoc />
    public LocalDate PreviousBusinessDay(LocalDate from)
    {
        var prev = from.AddDays(-1);
        // Max 2 iterations (Sunday -> Friday)
        while (prev.IsWeekend)
            prev = prev.AddDays(-1);
        return prev;
    }

    /// <inheritdoc />
    public LocalDate PreviousBusinessDay(LocalDate from, string countryCode)
    {
        var prev = from.AddDays(-1);
        while (prev.IsWeekend || _holidayProvider.IsHoliday(prev, countryCode))
            prev = prev.AddDays(-1);
        return prev;
    }

    #endregion

    #region Periods

    /// <inheritdoc />
    public LocalDate StartOfWeek(LocalDate date, DayOfWeek firstDay = DayOfWeek.Monday)
    {
        return date.StartOfWeek(firstDay);
    }

    /// <inheritdoc />
    public LocalDate EndOfWeek(LocalDate date, DayOfWeek firstDay = DayOfWeek.Monday)
    {
        return date.EndOfWeek(firstDay);
    }

    /// <inheritdoc />
    public LocalDate StartOfMonth(LocalDate date)
    {
        return date.StartOfMonth();
    }

    /// <inheritdoc />
    public LocalDate EndOfMonth(LocalDate date)
    {
        return date.EndOfMonth();
    }

    /// <inheritdoc />
    public LocalDate StartOfQuarter(LocalDate date)
    {
        return date.StartOfQuarter();
    }

    /// <inheritdoc />
    public LocalDate EndOfQuarter(LocalDate date)
    {
        return date.EndOfQuarter();
    }

    /// <inheritdoc />
    public LocalDate StartOfYear(LocalDate date)
    {
        return date.StartOfYear();
    }

    /// <inheritdoc />
    public LocalDate EndOfYear(LocalDate date)
    {
        return date.EndOfYear();
    }

    #endregion
}
