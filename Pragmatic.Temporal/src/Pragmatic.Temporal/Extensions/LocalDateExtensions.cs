using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Extensions;

/// <summary>
///     Extension methods for <see cref="LocalDate" /> providing calendar arithmetic
///     and relative date navigation.
/// </summary>
public static partial class LocalDateExtensions
{
    #region Period Arithmetic

    extension(LocalDate date)
    {
        /// <summary>
        ///     Adds a period (years, months, days) to this date.
        /// </summary>
        /// <remarks>
        ///     When adding months, if the resulting day is invalid (e.g., Jan 31 + 1 month),
        ///     it is clamped to the last valid day of the month (Feb 28/29).
        /// </remarks>
        public LocalDate Add(Period period)
        {
            // Add years and months first
            var result = date
                .AddYears(period.Years)
                .AddMonths(period.Months);

            // Then add days
            return result.AddDays(period.Days);
        }

        /// <summary>
        ///     Subtracts a period (years, months, days) from this date.
        /// </summary>
        public LocalDate Subtract(Period period)
        {
            return date.Add(period.Negate());
        }
    }

    #endregion

    #region Relative Day Navigation

    /// <param name="date">The reference date.</param>
    extension(LocalDate date)
    {
        /// <summary>
        ///     Returns the next occurrence of the specified day of week after this date.
        /// </summary>
        /// <param name="dayOfWeek">The target day of week.</param>
        /// <returns>The next date that falls on the specified day of week.</returns>
        /// <example>
        ///     <code>
        ///     // If today is Wednesday Jan 15, 2025
        ///     var nextFriday = today.Next(DayOfWeek.Friday); // Jan 17, 2025
        ///     var nextWednesday = today.Next(DayOfWeek.Wednesday); // Jan 22, 2025 (next week)
        ///     </code>
        /// </example>
        public LocalDate Next(DayOfWeek dayOfWeek)
        {
            var daysUntil = ((int)dayOfWeek - (int)date.DayOfWeek + 7) % 7;
            if (daysUntil == 0)
                daysUntil = 7; // Same day means next week
            return date.AddDays(daysUntil);
        }

        /// <summary>
        ///     Returns this date if it falls on the specified day of week,
        ///     otherwise the next occurrence.
        /// </summary>
        public LocalDate NextOrSame(DayOfWeek dayOfWeek)
        {
            if (date.DayOfWeek == dayOfWeek)
                return date;
            return date.Next(dayOfWeek);
        }

        /// <summary>
        ///     Returns the previous occurrence of the specified day of week before this date.
        /// </summary>
        /// <param name="dayOfWeek">The target day of week.</param>
        /// <returns>The previous date that falls on the specified day of week.</returns>
        /// <example>
        ///     <code>
        ///     // If today is Wednesday Jan 15, 2025
        ///     var prevMonday = today.Previous(DayOfWeek.Monday); // Jan 13, 2025
        ///     var prevWednesday = today.Previous(DayOfWeek.Wednesday); // Jan 8, 2025 (last week)
        ///     </code>
        /// </example>
        public LocalDate Previous(DayOfWeek dayOfWeek)
        {
            var daysSince = ((int)date.DayOfWeek - (int)dayOfWeek + 7) % 7;
            if (daysSince == 0)
                daysSince = 7; // Same day means last week
            return date.AddDays(-daysSince);
        }

        /// <summary>
        ///     Returns this date if it falls on the specified day of week,
        ///     otherwise the previous occurrence.
        /// </summary>
        public LocalDate PreviousOrSame(DayOfWeek dayOfWeek)
        {
            if (date.DayOfWeek == dayOfWeek)
                return date;
            return date.Previous(dayOfWeek);
        }
    }

    #endregion

    #region ISO Week

    extension(LocalDate date)
    {
        /// <summary>
        ///     Gets the ISO 8601 week number for this date.
        /// </summary>
        /// <remarks>
        ///     ISO weeks start on Monday. Week 1 is the week containing the first Thursday of the year.
        /// </remarks>
        public int IsoWeekOfYear()
        {
            return System.Globalization.ISOWeek.GetWeekOfYear(date.ToDateTime());
        }

        /// <summary>
        ///     Gets the ISO 8601 year for this date's week.
        /// </summary>
        /// <remarks>
        ///     The ISO week year may differ from the calendar year for dates near year boundaries.
        ///     For example, Dec 31, 2024 may be in ISO week year 2025.
        /// </remarks>
        public int IsoWeekYear()
        {
            return System.Globalization.ISOWeek.GetYear(date.ToDateTime());
        }
    }

    /// <summary>
    ///     Creates a LocalDate from an ISO week date (year, week, day of week).
    /// </summary>
    /// <param name="year">The ISO week year.</param>
    /// <param name="week">The ISO week number (1-52 or 53).</param>
    /// <param name="dayOfWeek">The day of week (Monday=1 to Sunday=7 in ISO).</param>
    public static LocalDate FromIsoWeekDate(int year, int week, DayOfWeek dayOfWeek)
    {
        // Convert DayOfWeek to ISO day (Monday=1, Sunday=7)
        var isoDay = dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
        var dateTime = System.Globalization.ISOWeek.ToDateTime(year, week, (DayOfWeek)((isoDay % 7)));
        return LocalDate.FromDateTime(dateTime);
    }

    #endregion
}
