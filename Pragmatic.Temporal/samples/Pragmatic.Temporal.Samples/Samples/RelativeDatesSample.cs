using Pragmatic.Temporal.Extensions;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

/// <summary>
///     Demonstrates relative date navigation (Next/Previous day of week, NthInMonth, etc.).
/// </summary>
public static class RelativeDatesSample
{
    public static void Run()
    {
        Console.WriteLine("--- Relative Dates Sample ---\n");

        // Reference date: Wednesday, January 15, 2025
        var wednesday = new LocalDate(2025, 1, 15);
        Console.WriteLine($"Reference date: {wednesday} ({wednesday.DayOfWeek})");

        // Next day of week
        Console.WriteLine("\n--- Next Day of Week ---");
        Console.WriteLine($"Next Monday: {wednesday.Next(DayOfWeek.Monday)}");
        Console.WriteLine($"Next Friday: {wednesday.Next(DayOfWeek.Friday)}");
        Console.WriteLine($"Next Wednesday: {wednesday.Next(DayOfWeek.Wednesday)}"); // Skips to next week

        // NextOrSame (returns same date if already that day)
        Console.WriteLine($"Next or Same Wednesday: {wednesday.NextOrSame(DayOfWeek.Wednesday)}"); // Same date

        // Previous day of week
        Console.WriteLine("\n--- Previous Day of Week ---");
        Console.WriteLine($"Previous Monday: {wednesday.Previous(DayOfWeek.Monday)}");
        Console.WriteLine($"Previous Friday: {wednesday.Previous(DayOfWeek.Friday)}");

        // First/Last in month
        Console.WriteLine("\n--- First/Last in Month ---");
        var jan2025 = new LocalDate(2025, 1, 15);
        Console.WriteLine($"First Monday of Jan 2025: {jan2025.FirstInMonth(DayOfWeek.Monday)}");
        Console.WriteLine($"Last Friday of Jan 2025: {jan2025.LastInMonth(DayOfWeek.Friday)}");

        // NthInMonth - for patterns like Patch Tuesday, Thanksgiving
        Console.WriteLine("\n--- Nth Day of Week in Month ---");

        // Patch Tuesday (Microsoft updates) - 2nd Tuesday of the month
        var patchTuesday = jan2025.NthInMonth(2, DayOfWeek.Tuesday);
        Console.WriteLine($"Patch Tuesday (2nd Tuesday): {patchTuesday}");

        // US Thanksgiving - 4th Thursday in November
        var nov2025 = new LocalDate(2025, 11, 1);
        var thanksgiving = nov2025.NthInMonth(4, DayOfWeek.Thursday);
        Console.WriteLine($"US Thanksgiving 2025 (4th Thursday): {thanksgiving}");

        // Last Sunday (negative index counts from end)
        var lastSunday = jan2025.NthInMonth(-1, DayOfWeek.Sunday);
        Console.WriteLine($"Last Sunday of Jan 2025: {lastSunday}");

        // 5th occurrence (may not exist)
        var fifthMonday = jan2025.NthInMonth(5, DayOfWeek.Monday);
        Console.WriteLine($"5th Monday of Jan 2025: {(fifthMonday.HasValue ? fifthMonday.ToString() : "(doesn't exist)")}");

        // Year-based navigation
        Console.WriteLine("\n--- First/Last in Year ---");
        Console.WriteLine($"First Monday of 2025: {jan2025.FirstInYear(DayOfWeek.Monday)}");
        Console.WriteLine($"Last Friday of 2025: {jan2025.LastInYear(DayOfWeek.Friday)}");

        // Business day helpers
        Console.WriteLine("\n--- Business Day Helpers ---");
        var friday = new LocalDate(2025, 1, 17);
        var saturday = new LocalDate(2025, 1, 18);
        var sunday = new LocalDate(2025, 1, 19);

        Console.WriteLine($"{friday} ({friday.DayOfWeek}):");
        Console.WriteLine($"  Next weekday: {friday.NextWeekday()}");
        Console.WriteLine($"  Previous weekday: {friday.PreviousWeekday()}");

        Console.WriteLine($"{saturday} ({saturday.DayOfWeek}):");
        Console.WriteLine($"  Next weekday: {saturday.NextWeekday()}");
        Console.WriteLine($"  Next weekday or same: {saturday.NextWeekdayOrSame()}");
        Console.WriteLine($"  Nearest weekday: {saturday.NearestWeekday()}"); // Friday

        Console.WriteLine($"{sunday} ({sunday.DayOfWeek}):");
        Console.WriteLine($"  Nearest weekday: {sunday.NearestWeekday()}"); // Monday

        // ISO Week
        Console.WriteLine("\n--- ISO Week ---");
        var jan1_2025 = new LocalDate(2025, 1, 1);
        var dec31_2025 = new LocalDate(2025, 12, 31);

        Console.WriteLine($"{jan1_2025}: ISO Week {jan1_2025.IsoWeekOfYear()}, ISO Year {jan1_2025.IsoWeekYear()}");
        Console.WriteLine($"{dec31_2025}: ISO Week {dec31_2025.IsoWeekOfYear()}, ISO Year {dec31_2025.IsoWeekYear()}");
        Console.WriteLine("  (Note: Dec 31 may be in ISO week 1 of next year!)");

        // Real-world example: Meeting scheduler
        Console.WriteLine("\n--- Real-World Example: Recurring Meetings ---");
        var today = new LocalDate(2025, 1, 15);
        Console.WriteLine($"Today: {today}");

        // Schedule next team sync (every Monday)
        var nextTeamSync = today.Next(DayOfWeek.Monday);
        Console.WriteLine($"Next Monday team sync: {nextTeamSync}");

        // Monthly all-hands (first Friday of month)
        var nextAllHands = today.FirstInMonth(DayOfWeek.Friday);
        if (nextAllHands < today)
        {
            // Already passed this month, get next month's
            nextAllHands = today.AddMonths(1).FirstInMonth(DayOfWeek.Friday);
        }
        Console.WriteLine($"Next all-hands (1st Friday): {nextAllHands}");

        // Sprint planning (every other Monday - just show next 4)
        Console.WriteLine("\nSprint planning dates (bi-weekly Mondays):");
        var sprintDate = today.NextOrSame(DayOfWeek.Monday);
        for (var i = 0; i < 4; i++)
        {
            Console.WriteLine($"  Sprint {i + 1}: {sprintDate}");
            sprintDate = sprintDate.AddDays(14); // Every 2 weeks
        }

        Console.WriteLine();
    }
}
