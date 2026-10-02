using Pragmatic.Temporal.Calculator;
using Pragmatic.Temporal.Testing;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

/// <summary>
///     Demonstrates the <see cref="TestHolidayProvider" /> fluent builder directly:
///     building a custom holiday set, querying it, mixing countries, and feeding it
///     to a <see cref="TemporalCalculator" />. This is the provider used to make
///     business-day calculations deterministic in tests.
/// </summary>
public static class TestHolidayProviderSample
{
    public static void Run()
    {
        Console.WriteLine("--- TestHolidayProvider Sample ---\n");

        // Fluent builder: chain AddHoliday / AddHolidays for an ad-hoc calendar.
        var holidays = new TestHolidayProvider()
            .AddHoliday(2024, 12, 25, "IT")                       // Natale
            .AddHoliday(new LocalDate(2024, 12, 26), "IT")        // Santo Stefano
            .AddHolidays("IT",
                new LocalDate(2024, 1, 1),                        // Capodanno
                new LocalDate(2024, 4, 25));                      // Liberazione

        Console.WriteLine("Custom IT holidays for 2024:");
        foreach (var holiday in holidays.GetHolidays(2024, "IT").OrderBy(h => h.Date))
            Console.WriteLine($"  {holiday.Date} - {holiday.Name} ({holiday.Type})");

        // Direct membership checks (O(1) lookup).
        Console.WriteLine($"\nIs 2024-12-25 a holiday (IT)? {holidays.IsHoliday(new LocalDate(2024, 12, 25), "IT")}");
        Console.WriteLine($"Is 2024-07-15 a holiday (IT)? {holidays.IsHoliday(new LocalDate(2024, 7, 15), "IT")}");

        // Multiple countries coexist in one provider; SupportedCountries reflects what was added.
        holidays.AddHoliday(2024, 7, 4, "US"); // Independence Day
        Console.WriteLine($"\nSupported countries: {string.Join(", ", holidays.SupportedCountries.OrderBy(c => c))}");
        Console.WriteLine($"Is 2024-07-04 a holiday (US)? {holidays.IsHoliday(new LocalDate(2024, 7, 4), "US")}");
        Console.WriteLine($"Is 2024-07-04 a holiday (IT)? {holidays.IsHoliday(new LocalDate(2024, 7, 4), "IT")}");

        // Preset factories cover common national calendars without manual entry.
        var italianPreset = TestHolidayProvider.WithItalianHolidays(2024);
        Console.WriteLine($"\nItalian preset holiday count (2024): {italianPreset.GetHolidays(2024, "IT").Count()}");

        // Plugging the provider into the calculator makes business-day math holiday-aware.
        var calculator = new TemporalCalculator(holidays);
        var orderDate = new LocalDate(2024, 4, 24);  // Wednesday, day before Liberazione
        var delivery = calculator.AddBusinessDays(orderDate, 2, "IT");
        Console.WriteLine(
            $"\n{orderDate} + 2 business days (IT, skipping 04-25): {delivery} ({delivery.DayOfWeek})");

        // Clear() resets the builder so a single instance can be reused across scenarios.
        holidays.Clear();
        Console.WriteLine($"\nAfter Clear(): supported countries = {holidays.SupportedCountries.Count()}");

        Console.WriteLine();
    }
}
