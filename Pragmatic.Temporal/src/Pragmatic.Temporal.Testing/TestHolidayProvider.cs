using Pragmatic.Temporal.Holidays;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Testing;

/// <summary>
///     A holiday provider for testing that allows easy configuration of holidays.
/// </summary>
public sealed class TestHolidayProvider : IHolidayProvider
{
    private readonly HashSet<string> _countries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(LocalDate Date, string Country)> _holidays = new();

    /// <summary>Creates an empty test holiday provider.</summary>
    public TestHolidayProvider()
    {
    }

    /// <summary>Creates a test holiday provider with the specified holidays.</summary>
    public TestHolidayProvider(params (LocalDate Date, string Country)[] holidays)
    {
        foreach (var holiday in holidays)
            AddHoliday(holiday.Date, holiday.Country);
    }

    /// <inheritdoc />
    public IEnumerable<Holiday> GetHolidays(int year, string countryCode)
    {
        var country = countryCode.ToUpperInvariant();
        return _holidays
            .Where(h => h.Date.Year == year && h.Country == country)
            .Select(h => new Holiday(h.Date, "Test Holiday", HolidayType.Public));
    }

    /// <inheritdoc />
    public IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode)
    {
        return GetHolidays(year, countryCode);
    }

    /// <inheritdoc />
    public bool IsHoliday(LocalDate date, string countryCode)
    {
        return _holidays.Contains((date, countryCode.ToUpperInvariant()));
    }

    /// <inheritdoc />
    public IEnumerable<string> SupportedCountries => _countries;

    /// <summary>Adds a holiday.</summary>
    public TestHolidayProvider AddHoliday(LocalDate date, string countryCode)
    {
        _holidays.Add((date, countryCode.ToUpperInvariant()));
        _countries.Add(countryCode.ToUpperInvariant());
        return this;
    }

    /// <summary>Adds a holiday.</summary>
    public TestHolidayProvider AddHoliday(int year, int month, int day, string countryCode)
    {
        return AddHoliday(new LocalDate(year, month, day), countryCode);
    }

    /// <summary>Adds multiple holidays.</summary>
    public TestHolidayProvider AddHolidays(string countryCode, params LocalDate[] dates)
    {
        foreach (var date in dates)
            AddHoliday(date, countryCode);
        return this;
    }

    /// <summary>Clears all holidays.</summary>
    public TestHolidayProvider Clear()
    {
        _holidays.Clear();
        _countries.Clear();
        return this;
    }

    #region Preset Configurations

    /// <summary>
    ///     Creates a provider with common Italian holidays for a year.
    /// </summary>
    public static TestHolidayProvider WithItalianHolidays(int year)
    {
        return new TestHolidayProvider()
            .AddHoliday(year, 1, 1, "IT") // Capodanno
            .AddHoliday(year, 1, 6, "IT") // Epifania
            .AddHoliday(year, 4, 25, "IT") // Liberazione
            .AddHoliday(year, 5, 1, "IT") // Lavoro
            .AddHoliday(year, 6, 2, "IT") // Repubblica
            .AddHoliday(year, 8, 15, "IT") // Ferragosto
            .AddHoliday(year, 11, 1, "IT") // Ognissanti
            .AddHoliday(year, 12, 8, "IT") // Immacolata
            .AddHoliday(year, 12, 25, "IT") // Natale
            .AddHoliday(year, 12, 26, "IT");
        // Santo Stefano
    }

    /// <summary>
    ///     Creates a provider with common US federal holidays for a year.
    /// </summary>
    public static TestHolidayProvider WithUsHolidays(int year)
    {
        return new TestHolidayProvider()
            .AddHoliday(year, 1, 1, "US") // New Year's Day
            .AddHoliday(year, 7, 4, "US") // Independence Day
            .AddHoliday(year, 11, 11, "US") // Veterans Day
            .AddHoliday(year, 12, 25, "US");
        // Christmas
    }

    #endregion
}