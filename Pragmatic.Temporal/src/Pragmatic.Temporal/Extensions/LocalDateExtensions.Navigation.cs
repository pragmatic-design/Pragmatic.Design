using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Extensions;

/// <summary>
///     Month-, year- and business-day navigation extensions for <see cref="LocalDate" />.
/// </summary>
public static partial class LocalDateExtensions
{
    #region Month-Based Navigation

    /// <param name="date">The reference date (determines the month).</param>
    extension(LocalDate date)
    {
        /// <summary>
        ///     Returns the first occurrence of the specified day of week in this date's month.
        /// </summary>
        /// <example>
        ///     <code>
        ///     var firstMonday = new LocalDate(2025, 1, 15).FirstInMonth(DayOfWeek.Monday); // Jan 6, 2025
        ///     </code>
        /// </example>
        public LocalDate FirstInMonth(DayOfWeek dayOfWeek)
        {
            var firstOfMonth = date.StartOfMonth();
            return firstOfMonth.NextOrSame(dayOfWeek);
        }

        /// <summary>
        ///     Returns the last occurrence of the specified day of week in this date's month.
        /// </summary>
        /// <example>
        ///     <code>
        ///     var lastFriday = new LocalDate(2025, 1, 15).LastInMonth(DayOfWeek.Friday); // Jan 31, 2025
        ///     </code>
        /// </example>
        public LocalDate LastInMonth(DayOfWeek dayOfWeek)
        {
            var lastOfMonth = date.EndOfMonth();
            return lastOfMonth.PreviousOrSame(dayOfWeek);
        }

        /// <summary>
        ///     Returns the Nth occurrence of the specified day of week in this date's month.
        /// </summary>
        /// <param name="n">The occurrence number (1 = first, 2 = second, etc.). Use -1 for last.</param>
        /// <param name="dayOfWeek">The target day of week.</param>
        /// <returns>The Nth occurrence, or null if it doesn't exist in the month.</returns>
        /// <example>
        ///     <code>
        ///     // Patch Tuesday (second Tuesday of the month)
        ///     var patchTuesday = today.NthInMonth(2, DayOfWeek.Tuesday);
        ///
        ///     // Thanksgiving (fourth Thursday in November, US)
        ///     var thanksgiving = new LocalDate(2025, 11, 1).NthInMonth(4, DayOfWeek.Thursday);
        ///
        ///     // Last Sunday of the month
        ///     var lastSunday = today.NthInMonth(-1, DayOfWeek.Sunday);
        ///     </code>
        /// </example>
        public LocalDate? NthInMonth(int n, DayOfWeek dayOfWeek)
        {
            if (n == 0)
                throw new ArgumentOutOfRangeException(nameof(n), "N must be non-zero. Use 1 for first, -1 for last.");

            if (n == -1)
                return date.LastInMonth(dayOfWeek);

            if (n < -1)
            {
                // Count backwards from the last
                var last = date.LastInMonth(dayOfWeek);
                var result = last.AddDays((n + 1) * 7); // n=-2 means 1 week before last
                return result.Month == date.Month ? result : null;
            }

            // Positive n: count forwards from first
            var first = date.FirstInMonth(dayOfWeek);
            var target = first.AddDays((n - 1) * 7);

            // Check if still in the same month
            return target.Month == date.Month ? target : null;
        }

        /// <summary>
        ///     Returns the Nth occurrence of the specified day of week in this date's month.
        ///     Throws if the occurrence doesn't exist.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the Nth occurrence doesn't exist in the month.</exception>
        public LocalDate NthInMonthOrThrow(int n, DayOfWeek dayOfWeek)
        {
            return date.NthInMonth(n, dayOfWeek)
                   ?? throw new ArgumentOutOfRangeException(nameof(n),
                       $"The {FormatOrdinal(n)} {dayOfWeek} does not exist in {date.Year}-{date.Month:D2}.");
        }
    }

    #endregion

    #region Year-Based Navigation

    extension(LocalDate date)
    {
        /// <summary>
        ///     Returns the first occurrence of the specified day of week in this date's year.
        /// </summary>
        public LocalDate FirstInYear(DayOfWeek dayOfWeek)
        {
            var firstOfYear = date.StartOfYear();
            return firstOfYear.NextOrSame(dayOfWeek);
        }

        /// <summary>
        ///     Returns the last occurrence of the specified day of week in this date's year.
        /// </summary>
        public LocalDate LastInYear(DayOfWeek dayOfWeek)
        {
            var lastOfYear = date.EndOfYear();
            return lastOfYear.PreviousOrSame(dayOfWeek);
        }
    }

    #endregion

    #region Business Day Helpers

    extension(LocalDate date)
    {
        /// <summary>
        ///     Returns the next weekday (Monday-Friday) after this date.
        /// </summary>
        public LocalDate NextWeekday()
        {
            var next = date.AddDays(1);
            while (next.IsWeekend)
                next = next.AddDays(1);
            return next;
        }

        /// <summary>
        ///     Returns this date if it's a weekday, otherwise the next weekday.
        /// </summary>
        public LocalDate NextWeekdayOrSame()
        {
            return date.IsWeekday ? date : date.NextWeekday();
        }

        /// <summary>
        ///     Returns the previous weekday (Monday-Friday) before this date.
        /// </summary>
        public LocalDate PreviousWeekday()
        {
            var prev = date.AddDays(-1);
            while (prev.IsWeekend)
                prev = prev.AddDays(-1);
            return prev;
        }

        /// <summary>
        ///     Returns this date if it's a weekday, otherwise the previous weekday.
        /// </summary>
        public LocalDate PreviousWeekdayOrSame()
        {
            return date.IsWeekday ? date : date.PreviousWeekday();
        }

        /// <summary>
        ///     Returns the nearest weekday to this date.
        ///     If this date is Saturday, returns Friday.
        ///     If this date is Sunday, returns Monday.
        /// </summary>
        public LocalDate NearestWeekday()
        {
            return date.DayOfWeek switch
            {
                DayOfWeek.Saturday => date.AddDays(-1),
                DayOfWeek.Sunday => date.AddDays(1),
                _ => date
            };
        }
    }

    #endregion

    #region Helpers

    private static string FormatOrdinal(int n)
    {
        if (n < 0)
            return $"{n}th from last";

        var abs = Math.Abs(n);
        var suffix = (abs % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (abs % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            }
        };
        return $"{n}{suffix}";
    }

    #endregion
}
