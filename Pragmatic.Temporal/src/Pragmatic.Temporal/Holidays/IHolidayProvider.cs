using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Holidays;

/// <summary>
///     Provides holiday information for business day calculations.
/// </summary>
public interface IHolidayProvider
{
    /// <summary>
    ///     Gets the list of supported country codes.
    /// </summary>
    IEnumerable<string> SupportedCountries { get; }

    /// <summary>
    ///     Gets the holidays for a specific year and country.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 country code (e.g., "IT", "US").</param>
    /// <returns>The holidays for that year and country.</returns>
    IEnumerable<Holiday> GetHolidays(int year, string countryCode);

    /// <summary>
    ///     Gets the holidays for a specific year, country, and region.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 country code.</param>
    /// <param name="regionCode">Optional region code (e.g., "MI" for Milan).</param>
    /// <returns>The holidays for that year, country, and region.</returns>
    IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode);

    /// <summary>
    ///     Checks if a date is a holiday.
    /// </summary>
    /// <param name="date">The date to check.</param>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 country code.</param>
    /// <returns>True if the date is a holiday.</returns>
    bool IsHoliday(LocalDate date, string countryCode);
}