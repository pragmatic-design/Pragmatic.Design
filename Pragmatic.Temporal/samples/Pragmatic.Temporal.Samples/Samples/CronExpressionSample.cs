using Pragmatic.Temporal.Testing;
using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

public static class CronExpressionSample
{
    public static void Run()
    {
        Console.WriteLine("--- CronExpression Sample ---\n");

        // Common cron expressions
        Console.WriteLine("Built-in expressions:");
        Console.WriteLine($"  Every minute: {CronExpression.EveryMinute}");
        Console.WriteLine($"  Every hour: {CronExpression.EveryHour}");
        Console.WriteLine($"  Daily at midnight: {CronExpression.Midnight}");
        Console.WriteLine($"  Weekdays at midnight: {CronExpression.Weekdays}");

        // Factory methods
        var dailyAt9 = CronExpression.Daily(new TimeOnly(9, 0));
        var every15Min = CronExpression.EveryMinutes(15);
        var weeklyMonday = CronExpression.Weekly(DayOfWeek.Monday, new TimeOnly(10, 0));

        Console.WriteLine("\nFactory methods:");
        Console.WriteLine($"  Daily at 9:00: {dailyAt9}");
        Console.WriteLine($"  Every 15 minutes: {every15Min}");
        Console.WriteLine($"  Weekly Monday at 10:00: {weeklyMonday}");

        // Parsing custom expressions
        var cron = CronExpression.Parse("0 9 * * 1-5"); // 9 AM on weekdays
        Console.WriteLine($"\nParsed: '{cron.Expression}'");

        // Get next occurrences
        var clock = new TestClock();
        clock.SetDateTime(2024, 1, 15, 8, 0); // Monday 8 AM

        Console.WriteLine($"\nNext 5 occurrences of '{cron.Expression}' from {clock.UtcNow}:");
        var occurrences = cron.GetOccurrences(clock.UtcNow).Take(5);
        foreach (var occurrence in occurrences)
            Console.WriteLine($"  {occurrence:yyyy-MM-dd HH:mm} ({occurrence.DayOfWeek})");

        // DST handling
        Console.WriteLine("\n--- Cron and DST ---");
        var nightlyCron = CronExpression.Parse("30 2 * * *"); // 2:30 AM daily
        var romeZone = TimeZoneResolver.GetTimeZone("Europe/Rome");

        clock.SetBeforeRomeSpringForward();
        var beforeDst = nightlyCron.GetNextOccurrence(clock.UtcNow, romeZone);
        Console.WriteLine(
            $"Before DST change: {(beforeDst.HasValue ? beforeDst.Value.ToString() : "SKIPPED (time doesn't exist)")}");

        // 2:30 AM doesn't exist on March 31 in Rome, so it's skipped

        // Matching
        var noon = CronExpression.Noon;
        var testTime = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
        Console.WriteLine($"\nDoes '{noon}' match {testTime}? {noon.Matches(testTime)}");

        Console.WriteLine();
    }
}