using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Calculator;

/// <summary>
///     Provides business day calculations including holiday awareness.
/// </summary>
public interface ITemporalCalculator
{
    #region Business Days

    /// <summary>
    ///     Adds business days (excludes weekends only).
    /// </summary>
    /// <param name="from">The starting date.</param>
    /// <param name="days">The number of business days to add (can be negative).</param>
    /// <returns>The resulting date.</returns>
    LocalDate AddBusinessDays(LocalDate from, int days);

    /// <summary>
    ///     Adds business days (excludes weekends and holidays for the specified country).
    /// </summary>
    /// <param name="from">The starting date.</param>
    /// <param name="days">The number of business days to add (can be negative).</param>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 country code.</param>
    /// <returns>The resulting date.</returns>
    LocalDate AddBusinessDays(LocalDate from, int days, string countryCode);

    /// <summary>
    ///     Adds business days (excludes weekends and the specified holidays).
    /// </summary>
    /// <param name="from">The starting date.</param>
    /// <param name="days">The number of business days to add (can be negative).</param>
    /// <param name="holidays">The holidays to exclude.</param>
    /// <returns>The resulting date.</returns>
    LocalDate AddBusinessDays(LocalDate from, int days, IEnumerable<LocalDate> holidays);

    /// <summary>
    ///     Counts business days between two dates (excludes weekends only).
    /// </summary>
    /// <param name="from">The start date (inclusive).</param>
    /// <param name="to">The end date (exclusive).</param>
    /// <returns>The number of business days.</returns>
    int CountBusinessDays(LocalDate from, LocalDate to);

    /// <summary>
    ///     Counts business days between two dates (excludes weekends and holidays).
    /// </summary>
    /// <param name="from">The start date (inclusive).</param>
    /// <param name="to">The end date (exclusive).</param>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 country code.</param>
    /// <returns>The number of business days.</returns>
    int CountBusinessDays(LocalDate from, LocalDate to, string countryCode);

    #endregion

    #region Checks

    /// <summary>
    ///     Checks if the date is a business day (not weekend).
    /// </summary>
    bool IsBusinessDay(LocalDate date);

    /// <summary>
    ///     Checks if the date is a business day (not weekend, not holiday).
    /// </summary>
    bool IsBusinessDay(LocalDate date, string countryCode);

    /// <summary>
    ///     Checks if the date is a holiday.
    /// </summary>
    bool IsHoliday(LocalDate date, string countryCode);

    /// <summary>
    ///     Checks if the date is a weekend (Saturday or Sunday).
    /// </summary>
    bool IsWeekend(LocalDate date);

    #endregion

    #region Navigation

    /// <summary>
    ///     Gets the next business day (excludes weekends only).
    /// </summary>
    LocalDate NextBusinessDay(LocalDate from);

    /// <summary>
    ///     Gets the next business day (excludes weekends and holidays).
    /// </summary>
    LocalDate NextBusinessDay(LocalDate from, string countryCode);

    /// <summary>
    ///     Gets the previous business day (excludes weekends only).
    /// </summary>
    LocalDate PreviousBusinessDay(LocalDate from);

    /// <summary>
    ///     Gets the previous business day (excludes weekends and holidays).
    /// </summary>
    LocalDate PreviousBusinessDay(LocalDate from, string countryCode);

    #endregion

    #region Periods

    /// <summary>Gets the first day of the week containing the date.</summary>
    LocalDate StartOfWeek(LocalDate date, DayOfWeek firstDay = DayOfWeek.Monday);

    /// <summary>Gets the last day of the week containing the date.</summary>
    LocalDate EndOfWeek(LocalDate date, DayOfWeek firstDay = DayOfWeek.Monday);

    /// <summary>Gets the first day of the month.</summary>
    LocalDate StartOfMonth(LocalDate date);

    /// <summary>Gets the last day of the month.</summary>
    LocalDate EndOfMonth(LocalDate date);

    /// <summary>Gets the first day of the quarter.</summary>
    LocalDate StartOfQuarter(LocalDate date);

    /// <summary>Gets the last day of the quarter.</summary>
    LocalDate EndOfQuarter(LocalDate date);

    /// <summary>Gets the first day of the year.</summary>
    LocalDate StartOfYear(LocalDate date);

    /// <summary>Gets the last day of the year.</summary>
    LocalDate EndOfYear(LocalDate date);

    #endregion
}