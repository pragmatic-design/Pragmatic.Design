using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Holidays;

/// <summary>
///     Holiday provider that uses a static list of holidays.
///     Useful for testing or when holidays are loaded from configuration.
/// </summary>
/// <remarks>
///     <para>
///         Not thread-safe for concurrent mutation: populate it (constructor or
///         <see cref="AddHoliday" />/<see cref="AddHolidays" />) before sharing it across
///         threads, e.g. at startup. Reads are safe once population is complete.
///     </para>
///     <para>
///         The <c>regionCode</c> parameter of <see cref="GetHolidays(int, string, string?)" />
///         is currently ignored: holidays are tracked per country only.
///     </para>
/// </remarks>
public sealed class StaticHolidayProvider : IHolidayProvider
{
    private readonly HashSet<string> _countries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Year, string CountryCode), List<Holiday>> _holidays = new();
    private readonly Dictionary<(int Year, string CountryCode), HashSet<LocalDate>> _holidayDates = new();

    /// <summary>
    ///     Creates an empty StaticHolidayProvider.
    /// </summary>
    public StaticHolidayProvider()
    {
    }

    /// <summary>
    ///     Creates a StaticHolidayProvider with the specified holidays.
    /// </summary>
    public StaticHolidayProvider(IEnumerable<(string CountryCode, Holiday Holiday)> holidays)
    {
        foreach (var (countryCode, holiday) in holidays)
            AddHoliday(countryCode, holiday);
    }

    /// <inheritdoc />
    public IEnumerable<Holiday> GetHolidays(int year, string countryCode)
    {
        var key = (year, countryCode.ToUpperInvariant());
        return _holidays.TryGetValue(key, out var list)
            ? list.AsReadOnly()
            : Enumerable.Empty<Holiday>();
    }

    /// <inheritdoc />
    public IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode)
    {
        return GetHolidays(year, countryCode);
    }

    /// <inheritdoc />
    public bool IsHoliday(LocalDate date, string countryCode)
    {
        var key = (date.Year, countryCode.ToUpperInvariant());
        return _holidayDates.TryGetValue(key, out var set) && set.Contains(date);
    }

    /// <inheritdoc />
    /// <remarks>Returns a snapshot — mutating the provider does not affect previously returned sequences.</remarks>
    public IEnumerable<string> SupportedCountries => _countries.ToArray();

    /// <summary>
    ///     Adds a holiday to the provider.
    /// </summary>
    public void AddHoliday(string countryCode, Holiday holiday)
    {
        var key = (holiday.Date.Year, countryCode.ToUpperInvariant());
        if (!_holidays.TryGetValue(key, out var list))
        {
            list = new List<Holiday>();
            _holidays[key] = list;
        }

        list.Add(holiday);

        if (!_holidayDates.TryGetValue(key, out var set))
        {
            set = new HashSet<LocalDate>();
            _holidayDates[key] = set;
        }
        set.Add(holiday.Date);

        _countries.Add(countryCode.ToUpperInvariant());
    }

    /// <summary>
    ///     Adds multiple holidays for a country.
    /// </summary>
    public void AddHolidays(string countryCode, IEnumerable<Holiday> holidays)
    {
        foreach (var holiday in holidays)
            AddHoliday(countryCode, holiday);
    }

    /// <summary>
    ///     Creates a builder for fluent configuration.
    /// </summary>
    public static Builder CreateBuilder()
    {
        return new Builder();
    }

    /// <summary>
    ///     Fluent builder for StaticHolidayProvider.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<(string CountryCode, Holiday Holiday)> _holidays = new();

        /// <summary>Adds a holiday.</summary>
        public Builder AddHoliday(string countryCode, LocalDate date, string name,
            HolidayType type = HolidayType.Public)
        {
            _holidays.Add((countryCode, new Holiday(date, name, type)));
            return this;
        }

        /// <summary>Adds a holiday.</summary>
        public Builder AddHoliday(string countryCode, int year, int month, int day, string name,
            HolidayType type = HolidayType.Public)
        {
            return AddHoliday(countryCode, new LocalDate(year, month, day), name, type);
        }

        /// <summary>Builds the provider.</summary>
        public StaticHolidayProvider Build()
        {
            return new StaticHolidayProvider(_holidays);
        }
    }
}