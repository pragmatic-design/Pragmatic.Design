using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

/// <summary>
///     Demonstrates DateRange for working with date intervals.
/// </summary>
public static class DateRangeSample
{
    public static void Run()
    {
        Console.WriteLine("--- Date Range Sample ---\n");

        // Create ranges
        var january = DateRange.Month(2024, 1);
        var q1 = DateRange.Quarter(2024, 1);
        var year = DateRange.Year(2024);

        Console.WriteLine($"January 2024: {january} ({january.Days} days)");
        Console.WriteLine($"Q1 2024: {q1} ({q1.Days} days)");
        Console.WriteLine($"Year 2024: {year} ({year.Days} days)");

        // Week containing a date
        var wednesday = new LocalDate(2024, 1, 17);
        var week = DateRange.Week(wednesday);
        Console.WriteLine($"\nWeek containing {wednesday}: {week}");
        Console.WriteLine($"  Monday: {week.Start}");
        Console.WriteLine($"  Sunday: {week.End}");

        // Last/Next N days
        var today = new LocalDate(2024, 6, 15);
        var lastWeek = DateRange.LastDays(today, 7);
        var nextWeek = DateRange.NextDays(today, 7);
        Console.WriteLine($"\nLast 7 days from {today}: {lastWeek}");
        Console.WriteLine($"Next 7 days from {today}: {nextWeek}");

        // Contains operations
        var testDate = new LocalDate(2024, 1, 15);
        Console.WriteLine($"\n{january} contains {testDate}? {january.Contains(testDate)}");
        Console.WriteLine($"{january} contains {q1}? {q1.Contains(january)}");

        // Overlap detection (useful for scheduling conflicts)
        var meeting1 = new DateRange(new LocalDate(2024, 1, 10), new LocalDate(2024, 1, 20));
        var meeting2 = new DateRange(new LocalDate(2024, 1, 15), new LocalDate(2024, 1, 25));
        Console.WriteLine($"\n{meeting1} overlaps {meeting2}? {meeting1.Overlaps(meeting2)}");

        // Intersection
        var intersection = meeting1.Intersect(meeting2);
        Console.WriteLine($"Intersection: {intersection}");

        // Union
        var union = meeting1.Union(meeting2);
        Console.WriteLine($"Union: {union}");

        // Split into chunks (useful for batch processing)
        var year2024 = DateRange.Year(2024);
        var months = year2024.Split(31).Take(3).ToList(); // Approximately monthly chunks
        Console.WriteLine($"\nFirst 3 ~monthly chunks of 2024:");
        foreach (var month in months)
        {
            Console.WriteLine($"  {month} ({month.Days} days)");
        }

        // Enumeration
        var shortRange = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 5));
        Console.WriteLine($"\nDays in {shortRange}:");
        foreach (var date in shortRange)
        {
            Console.WriteLine($"  {date} ({date.DayOfWeek})");
        }

        // Weekdays only
        Console.WriteLine($"\nWeekdays in {shortRange}:");
        foreach (var date in shortRange.Weekdays())
        {
            Console.WriteLine($"  {date} ({date.DayOfWeek})");
        }

        // ISO 8601 parsing
        var parsed = DateRange.Parse("2024-01-01/2024-12-31");
        Console.WriteLine($"\nParsed range: {parsed}");

        Console.WriteLine();
    }
}
