using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

public static class CoreTypesSample
{
    public static void Run()
    {
        Console.WriteLine("--- Core Types Sample ---\n");

        // LocalDate - a date without timezone
        var today = new LocalDate(2024, 1, 15);
        Console.WriteLine($"Today: {today}");
        Console.WriteLine($"  Day of week: {today.DayOfWeek}");
        Console.WriteLine($"  Is weekend: {today.IsWeekend}");
        Console.WriteLine($"  Start of month: {today.StartOfMonth()}");
        Console.WriteLine($"  End of month: {today.EndOfMonth()}");
        Console.WriteLine($"  Quarter: {today.Quarter}");

        // LocalTime - a time without timezone
        var meetingTime = new LocalTime(14, 30);
        Console.WriteLine($"\nMeeting time: {meetingTime}");
        Console.WriteLine($"  Is afternoon: {meetingTime.IsAfternoon}");
        Console.WriteLine($"  Plus 1 hour: {meetingTime.AddHours(1)}");

        // LocalDateTime - date+time without timezone
        var appointment = today.At(meetingTime);
        Console.WriteLine($"\nAppointment: {appointment}");
        Console.WriteLine($"  Start of day: {appointment.StartOfDay()}");

        // Duration - physical elapsed time
        var duration = Duration.FromHours(2) + Duration.FromMinutes(30);
        Console.WriteLine($"\nDuration: {duration}");
        Console.WriteLine($"  Total minutes: {duration.TotalMinutes}");
        Console.WriteLine($"  As ISO 8601: {duration}");

        // Parsing
        var parsedDate = LocalDate.Parse("2024-06-15");
        var parsedTime = LocalTime.Parse("09:30:00");
        var parsedDuration = Duration.Parse("PT2H30M");
        Console.WriteLine($"\nParsed date: {parsedDate}");
        Console.WriteLine($"Parsed time: {parsedTime}");
        Console.WriteLine($"Parsed duration: {parsedDuration}");

        Console.WriteLine();
    }
}