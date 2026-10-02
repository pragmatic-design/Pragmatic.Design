using Pragmatic.Temporal.Holidays;
using Pragmatic.Temporal.Types;

namespace TimeOff.Leave.Infrastructure.Calendar;

/// <summary>
///     The Italian national holidays, computed for any year: the fixed dates, and Easter Monday from
///     the date of Easter.
/// </summary>
/// <remarks>
///     <para>
///         Computed rather than stored: the calendar is law, the same for every company, and a table
///         someone forgets to fill for next year is a request counted wrong in January. What differs
///         between companies — closures, the patron saint — is <c>CompanyHoliday</c>, which HR keeps.
///     </para>
///     <para>
///         4 October (Saint Francis) is a national holiday again from 2026: Legge 8 ottobre 2025,
///         n. 151 (Gazzetta Ufficiale n. 236 of 10 October 2025).
///     </para>
/// </remarks>
public sealed class ItalianPublicHolidays : IHolidayProvider
{
    public const string Country = "IT";

    public IEnumerable<string> SupportedCountries => [Country];

    public IEnumerable<Holiday> GetHolidays(int year, string countryCode) =>
        string.Equals(countryCode, Country, StringComparison.OrdinalIgnoreCase) ? For(year) : [];

    public IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode) =>
        GetHolidays(year, countryCode);

    public bool IsHoliday(LocalDate date, string countryCode) =>
        GetHolidays(date.Year, countryCode).Any(holiday => holiday.Date == date);

    private static IEnumerable<Holiday> For(int year)
    {
        yield return Holiday.Public(new LocalDate(year, 1, 1), "Capodanno");
        yield return Holiday.Public(new LocalDate(year, 1, 6), "Epifania");
        yield return Holiday.Public(EasterSunday(year).AddDays(1), "Lunedì dell'Angelo");
        yield return Holiday.Public(new LocalDate(year, 4, 25), "Festa della Liberazione");
        yield return Holiday.Public(new LocalDate(year, 5, 1), "Festa del Lavoro");
        yield return Holiday.Public(new LocalDate(year, 6, 2), "Festa della Repubblica");
        yield return Holiday.Public(new LocalDate(year, 8, 15), "Ferragosto");
        if (year >= 2026)
            yield return Holiday.Public(new LocalDate(year, 10, 4), "San Francesco d'Assisi");
        yield return Holiday.Public(new LocalDate(year, 11, 1), "Ognissanti");
        yield return Holiday.Public(new LocalDate(year, 12, 8), "Immacolata Concezione");
        yield return Holiday.Public(new LocalDate(year, 12, 25), "Natale");
        yield return Holiday.Public(new LocalDate(year, 12, 26), "Santo Stefano");
    }

    /// <summary>Easter Sunday in the Gregorian calendar (the anonymous algorithm, Meeus/Jones/Butcher).</summary>
    internal static LocalDate EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new LocalDate(year, month, day);
    }
}
