using Pragmatic.Temporal.Testing;
using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

public static class ZonedDateTimeSample
{
    public static void Run()
    {
        Console.WriteLine("--- ZonedDateTime Sample ---\n");

        var clock = new TestClock();
        clock.SetDateTime(2024, 7, 15, 14, 30);

        // Creating ZonedDateTime from UTC (safest)
        var romeTime = ZonedDateTime.FromUtc(clock.UtcNow, "Europe/Rome");
        Console.WriteLine($"UTC: {clock.UtcNow}");
        Console.WriteLine($"Rome: {romeTime}");
        Console.WriteLine($"  Zone ID: {romeTime.ZoneId}");
        Console.WriteLine($"  Is DST: {romeTime.IsDaylightSavingTime}");
        Console.WriteLine($"  Offset: {romeTime.Offset}");

        // Same instant in different timezone
        var newYorkTime = romeTime.InZone("America/New_York");
        Console.WriteLine($"\nSame instant in New York: {newYorkTime}");

        // Creating from local time (requires DST policy)
        var localDateTime = new DateTime(2024, 3, 31, 2, 30, 0); // This time doesn't exist in Rome!
        try
        {
            var zdt = ZonedDateTime.FromLocal(localDateTime, TimeZoneResolver.GetTimeZone("Europe/Rome"),
                NonExistentTimePolicy.ThrowException);
        }
        catch (NonExistentTimeException ex)
        {
            Console.WriteLine($"\nNon-existent time caught: {ex.Message.Split('.')[0]}");
        }

        // ShiftForward policy (default)
        var shifted = ZonedDateTime.FromLocal(localDateTime, TimeZoneResolver.GetTimeZone("Europe/Rome"));
        Console.WriteLine($"Shifted forward: {shifted}");

        // Calendar arithmetic vs physical duration
        Console.WriteLine("\n--- Calendar vs Physical ---");
        var march30Rome = ZonedDateTime.FromLocal(
            new DateTime(2024, 3, 30, 12, 0, 0),
            TimeZoneResolver.GetTimeZone("Europe/Rome"));

        // Calendar: "same time tomorrow"
        var tomorrow = march30Rome.AddDays(1);
        Console.WriteLine($"March 30 noon: {march30Rome}");
        Console.WriteLine($"Plus 1 calendar day: {tomorrow}");

        // Physical: exactly 24 hours
        var plus24H = march30Rome.Add(Duration.FromDays(1));
        Console.WriteLine($"Plus 24 physical hours: {plus24H}");

        // The difference: on DST change, they differ by 1 hour!
        var difference = plus24H - tomorrow;
        Console.WriteLine($"Difference: {difference} (because March 31 only has 23 hours in Rome)");

        Console.WriteLine();
    }
}