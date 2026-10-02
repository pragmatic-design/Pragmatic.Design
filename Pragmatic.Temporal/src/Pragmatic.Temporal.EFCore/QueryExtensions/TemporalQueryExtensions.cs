using System.Linq.Expressions;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Internal;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.QueryExtensions;

/// <summary>
///     LINQ query extensions for temporal filtering.
///     All methods generate UTC range conditions that use indexes efficiently.
/// </summary>
public static class TemporalQueryExtensions
{
    #region Range Filtering (Low-level)

    /// <summary>
    ///     Filters records where the datetime is within a UTC range [start, end).
    ///     This is the low-level method that generates index-friendly queries.
    /// </summary>
    public static IQueryable<T> WhereBetween<T>(
        this IQueryable<T> query,
        Expression<Func<T, DateTimeOffset>> selector,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var parameter = selector.Parameters[0];
        var property = selector.Body;

        // Build: x.Property >= start && x.Property < end
        var startConstant = Expression.Constant(start.ToUniversalTime());
        var endConstant = Expression.Constant(end.ToUniversalTime());

        var greaterOrEqual = Expression.GreaterThanOrEqual(property, startConstant);
        var lessThan = Expression.LessThan(property, endConstant);
        var combined = Expression.AndAlso(greaterOrEqual, lessThan);

        var lambda = Expression.Lambda<Func<T, bool>>(combined, parameter);
        return query.Where(lambda);
    }

    /// <summary>
    ///     Point-in-time ("as of") filter: keeps records whose datetime is at or before
    ///     <paramref name="moment" /> (selector &lt;= moment, in UTC). This is the primary
    ///     entry point for temporal as-of queries.
    /// </summary>
    /// <param name="query">The queryable.</param>
    /// <param name="selector">Property selector for the DateTimeOffset field.</param>
    /// <param name="moment">The point in time to evaluate the data as of.</param>
    public static IQueryable<T> AsOf<T>(
        this IQueryable<T> query,
        Expression<Func<T, DateTimeOffset>> selector,
        DateTimeOffset moment)
    {
        var parameter = selector.Parameters[0];
        var property = selector.Body;

        // Build: x.Property <= moment
        var momentConstant = Expression.Constant(moment.ToUniversalTime());
        var lessThanOrEqual = Expression.LessThanOrEqual(property, momentConstant);

        var lambda = Expression.Lambda<Func<T, bool>>(lessThanOrEqual, parameter);
        return query.Where(lambda);
    }

    #endregion

    #region Date Filtering

    /// <param name="query">The queryable.</param>
    /// <typeparam name="T">Entity type.</typeparam>
    extension<T>(IQueryable<T> query)
    {
        /// <summary>
        ///     Filters records where the datetime falls on a specific date in the given timezone.
        /// </summary>
        /// <param name="selector">Property selector for the DateTimeOffset field.</param>
        /// <param name="date">The date to filter by.</param>
        /// <param name="zone">The timezone to interpret the date in.</param>
        public IQueryable<T> WhereDate(Expression<Func<T, DateTimeOffset>> selector,
            LocalDate date,
            TimeZoneInfo zone)
        {
            var (start, end) = GetDayRange(date, zone);
            return query.WhereBetween(selector, start, end);
        }

        /// <summary>
        ///     Filters records where the datetime falls on "today" in the business timezone.
        /// </summary>
        public IQueryable<T> WhereToday(Expression<Func<T, DateTimeOffset>> selector,
            TemporalContext context)
        {
            var (start, end) = context.BusinessTodayRange;
            return query.WhereBetween(selector, start, end);
        }

        /// <summary>
        ///     Filters records where the datetime falls on "today" in the client timezone.
        /// </summary>
        public IQueryable<T> WhereClientToday(Expression<Func<T, DateTimeOffset>> selector,
            TemporalContext context)
        {
            var (start, end) = context.ClientTodayRange;
            return query.WhereBetween(selector, start, end);
        }

        /// <summary>
        ///     Filters records where the datetime falls between two dates (inclusive) in the given timezone.
        /// </summary>
        public IQueryable<T> WhereBetweenDates(Expression<Func<T, DateTimeOffset>> selector,
            LocalDate from,
            LocalDate to,
            TimeZoneInfo zone)
        {
            var start = GetStartOfDay(from, zone);
            var end = GetStartOfDay(to.AddDays(1), zone);
            return query.WhereBetween(selector, start, end);
        }
    }

    #endregion

    #region Period Filtering

    extension<T>(IQueryable<T> query)
    {
        /// <summary>
        ///     Filters records for a specific month in the given timezone.
        /// </summary>
        public IQueryable<T> WhereMonth(Expression<Func<T, DateTimeOffset>> selector,
            int year,
            int month,
            TimeZoneInfo zone)
        {
            var firstDay = new LocalDate(year, month, 1);
            var lastDay = firstDay.EndOfMonth();
            return query.WhereBetweenDates(selector, firstDay, lastDay, zone);
        }

        /// <summary>
        ///     Filters records for a specific year in the given timezone.
        /// </summary>
        public IQueryable<T> WhereYear(Expression<Func<T, DateTimeOffset>> selector,
            int year,
            TimeZoneInfo zone)
        {
            var firstDay = new LocalDate(year, 1, 1);
            var lastDay = new LocalDate(year, 12, 31);
            return query.WhereBetweenDates(selector, firstDay, lastDay, zone);
        }

        /// <summary>
        ///     Filters records for a specific quarter in the given timezone.
        /// </summary>
        public IQueryable<T> WhereQuarter(Expression<Func<T, DateTimeOffset>> selector,
            int year,
            int quarter,
            TimeZoneInfo zone)
        {
            if (quarter < 1 || quarter > 4)
                throw new ArgumentOutOfRangeException(nameof(quarter), "Quarter must be between 1 and 4.");

            var firstMonth = (quarter - 1) * 3 + 1;
            var firstDay = new LocalDate(year, firstMonth, 1);
            var lastDay = firstDay.AddMonths(3).AddDays(-1);
            return query.WhereBetweenDates(selector, firstDay, lastDay, zone);
        }

        /// <summary>
        ///     Filters records for a specific week in the given timezone (ISO 8601 week).
        /// </summary>
        public IQueryable<T> WhereWeek(Expression<Func<T, DateTimeOffset>> selector,
            int year,
            int week,
            TimeZoneInfo zone,
            DayOfWeek firstDayOfWeek = DayOfWeek.Monday)
        {
            // Get the first day of the year
            var jan1 = new LocalDate(year, 1, 1);

            // Find the first occurrence of firstDayOfWeek
            var daysUntilFirstDay = ((int)firstDayOfWeek - (int)jan1.DayOfWeek + 7) % 7;
            var firstWeekStart = jan1.AddDays(daysUntilFirstDay);

            // If the first week starts after Jan 4, go back a week (ISO 8601 rule)
            if (daysUntilFirstDay > 3)
                firstWeekStart = firstWeekStart.AddDays(-7);

            var weekStart = firstWeekStart.AddDays((week - 1) * 7);
            var weekEnd = weekStart.AddDays(6);

            return query.WhereBetweenDates(selector, weekStart, weekEnd, zone);
        }
    }

    #endregion

    #region Relative Filtering

    extension<T>(IQueryable<T> query)
    {
        /// <summary>
        ///     Filters records from the last N days (including today) in the business timezone.
        /// </summary>
        public IQueryable<T> WhereLast(Expression<Func<T, DateTimeOffset>> selector,
            int days,
            TemporalContext context)
        {
            var endDate = context.BusinessToday;
            var startDate = endDate.AddDays(-(days - 1));
            return query.WhereBetweenDates(selector, startDate, endDate, context.BusinessTimeZone);
        }

        /// <summary>
        ///     Filters records from the previous month in the business timezone.
        /// </summary>
        public IQueryable<T> WhereLastMonth(Expression<Func<T, DateTimeOffset>> selector,
            TemporalContext context)
        {
            var today = context.BusinessToday;
            var lastMonth = today.AddMonths(-1);
            return query.WhereMonth(selector, lastMonth.Year, lastMonth.Month, context.BusinessTimeZone);
        }

        /// <summary>
        ///     Filters records from the current month in the business timezone.
        /// </summary>
        public IQueryable<T> WhereThisMonth(Expression<Func<T, DateTimeOffset>> selector,
            TemporalContext context)
        {
            var today = context.BusinessToday;
            return query.WhereMonth(selector, today.Year, today.Month, context.BusinessTimeZone);
        }

        /// <summary>
        ///     Filters records from the current year in the business timezone.
        /// </summary>
        public IQueryable<T> WhereThisYear(Expression<Func<T, DateTimeOffset>> selector,
            TemporalContext context)
        {
            var today = context.BusinessToday;
            return query.WhereYear(selector, today.Year, context.BusinessTimeZone);
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    ///     Gets the UTC instant for the start of a day, using DST-safe offset calculation
    ///     that matches <see cref="TemporalContext.ClientStartOfDay" /> behavior.
    /// </summary>
    private static DateTimeOffset GetStartOfDay(LocalDate date, TimeZoneInfo zone)
    {
        var localMidnight = date.ToDateTime();
        // Midnight is rarely in a DST gap, but handle it properly via DstHelper
        return DstHelper.LocalToUtc(localMidnight, zone,
            NonExistentTimePolicy.ShiftForward, AmbiguousTimePolicy.UseStandardTime);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) GetDayRange(LocalDate date, TimeZoneInfo zone)
    {
        var start = GetStartOfDay(date, zone);
        var end = GetStartOfDay(date.AddDays(1), zone);
        return (start, end);
    }

    #endregion
}