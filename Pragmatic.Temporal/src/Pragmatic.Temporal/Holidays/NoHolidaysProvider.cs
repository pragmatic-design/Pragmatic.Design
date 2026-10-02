using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Holidays;

/// <summary>
///     Default holiday provider that returns no holidays.
///     Use this as a fallback or when holidays are not needed.
/// </summary>
public sealed class NoHolidaysProvider : IHolidayProvider
{
    private NoHolidaysProvider()
    {
    }

    /// <summary>
    ///     Gets the singleton instance.
    /// </summary>
    public static IHolidayProvider Instance { get; } = new NoHolidaysProvider();

    /// <inheritdoc />
    public IEnumerable<Holiday> GetHolidays(int year, string countryCode)
    {
        return Enumerable.Empty<Holiday>();
    }

    /// <inheritdoc />
    public IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode)
    {
        return Enumerable.Empty<Holiday>();
    }

    /// <inheritdoc />
    public bool IsHoliday(LocalDate date, string countryCode)
    {
        return false;
    }

    /// <inheritdoc />
    public IEnumerable<string> SupportedCountries => Enumerable.Empty<string>();
}