using Pragmatic.Temporal.Calculator;
using Pragmatic.Temporal.Testing;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

public static class BusinessDaysSample
{
    public static void Run()
    {
        Console.WriteLine("--- Business Days Sample ---\n");

        // Create calculator with Italian holidays
        var holidays = TestHolidayProvider.WithItalianHolidays(2024);
        var calculator = new TemporalCalculator(holidays);

        var orderDate = new LocalDate(2024, 4, 22); // Monday before Liberation Day (April 25)
        Console.WriteLine($"Order date: {orderDate} ({orderDate.DayOfWeek})");

        // Add business days (no holidays)
        var delivery5Days = calculator.AddBusinessDays(orderDate, 5);
        Console.WriteLine($"Plus 5 business days (no holidays): {delivery5Days} ({delivery5Days.DayOfWeek})");

        // Add business days (with Italian holidays)
        var deliveryItaly = calculator.AddBusinessDays(orderDate, 5, "IT");
        Console.WriteLine($"Plus 5 business days (IT holidays): {deliveryItaly} ({deliveryItaly.DayOfWeek})");
        Console.WriteLine("  Note: April 25 (Liberation Day) is skipped!");

        // Check specific dates
        var liberationDay = new LocalDate(2024, 4, 25);
        Console.WriteLine("\nApril 25, 2024:");
        Console.WriteLine($"  Is weekend: {calculator.IsWeekend(liberationDay)}");
        Console.WriteLine($"  Is holiday (IT): {calculator.IsHoliday(liberationDay, "IT")}");
        Console.WriteLine($"  Is business day (IT): {calculator.IsBusinessDay(liberationDay, "IT")}");

        // Count business days
        var startDate = new LocalDate(2024, 4, 1);
        var endDate = new LocalDate(2024, 4, 30);
        var businessDays = calculator.CountBusinessDays(startDate, endDate);
        var businessDaysIt = calculator.CountBusinessDays(startDate, endDate, "IT");
        Console.WriteLine("\nBusiness days in April 2024:");
        Console.WriteLine($"  Without holidays: {businessDays}");
        Console.WriteLine($"  With IT holidays: {businessDaysIt}");

        // Navigation
        var friday = new LocalDate(2024, 1, 19); // Friday
        Console.WriteLine($"\nFrom Friday {friday}:");
        Console.WriteLine($"  Next business day: {calculator.NextBusinessDay(friday)}");
        Console.WriteLine($"  Previous business day: {calculator.PreviousBusinessDay(friday)}");

        // Period helpers
        var today = new LocalDate(2024, 6, 15);
        Console.WriteLine($"\nPeriod helpers for {today}:");
        Console.WriteLine($"  Start of week: {calculator.StartOfWeek(today)}");
        Console.WriteLine($"  End of week: {calculator.EndOfWeek(today)}");
        Console.WriteLine($"  Start of month: {calculator.StartOfMonth(today)}");
        Console.WriteLine($"  End of month: {calculator.EndOfMonth(today)}");
        Console.WriteLine($"  Start of quarter: {calculator.StartOfQuarter(today)}");
        Console.WriteLine($"  End of quarter: {calculator.EndOfQuarter(today)}");

        Console.WriteLine();
    }
}