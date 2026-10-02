using Pragmatic.Temporal.Extensions;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

/// <summary>
///     Demonstrates Period for calendar-based duration arithmetic.
/// </summary>
public static class PeriodSample
{
    public static void Run()
    {
        Console.WriteLine("--- Period (Calendar Duration) Sample ---\n");

        // Create periods
        var oneYear = Period.FromYears(1);
        var sixMonths = Period.FromMonths(6);
        var twoWeeks = Period.FromWeeks(2); // Converted to 14 days
        var subscription = new Period(1, 0, 0); // 1 year subscription

        Console.WriteLine($"One year: {oneYear}");
        Console.WriteLine($"Six months: {sixMonths}");
        Console.WriteLine($"Two weeks: {twoWeeks} ({twoWeeks.Days} days)");
        Console.WriteLine($"Subscription: {subscription}");

        // ISO 8601 format
        var period = new Period(1, 6, 15);
        Console.WriteLine($"\nPeriod 1Y 6M 15D:");
        Console.WriteLine($"  ISO format: {period}");
        Console.WriteLine($"  Display: {period.ToDisplayString()}");

        // The "end of month" problem - Period handles it correctly!
        Console.WriteLine("\n--- End of Month Problem ---");
        var jan31 = new LocalDate(2024, 1, 31);
        var oneMonth = Period.FromMonths(1);

        var result = jan31.Add(oneMonth);
        Console.WriteLine($"{jan31} + 1 month = {result}");
        Console.WriteLine("  (Correctly handles month-end: Feb doesn't have 31 days!)");

        // Non-leap year
        var jan31_2023 = new LocalDate(2023, 1, 31);
        var result2 = jan31_2023.Add(oneMonth);
        Console.WriteLine($"{jan31_2023} + 1 month = {result2} (non-leap year)");

        // Period arithmetic
        Console.WriteLine("\n--- Period Arithmetic ---");
        var p1 = new Period(1, 2, 3);
        var p2 = new Period(0, 6, 10);
        Console.WriteLine($"{p1} + {p2} = {p1 + p2}");
        Console.WriteLine($"{p1} - {p2} = {p1 - p2}");
        Console.WriteLine($"{p1} * 2 = {p1 * 2}");
        Console.WriteLine($"Negated {p1} = {-p1}");

        // Normalize (excess months → years)
        var unnormalized = new Period(0, 18, 0);
        var normalized = unnormalized.Normalize();
        Console.WriteLine($"\n{unnormalized} normalized = {normalized}");

        // Calculate period between dates
        Console.WriteLine("\n--- Period Between Dates ---");
        var start = new LocalDate(2024, 1, 15);
        var end = new LocalDate(2025, 7, 20);
        var between = Period.Between(start, end);
        Console.WriteLine($"Between {start} and {end}:");
        Console.WriteLine($"  {between}");
        Console.WriteLine($"  {between.ToDisplayString()}");

        // Real-world example: subscription expiry
        Console.WriteLine("\n--- Subscription Example ---");
        var subscriptionStart = new LocalDate(2024, 3, 15);
        var annualPeriod = Period.FromYears(1);
        var expiryDate = subscriptionStart.Add(annualPeriod);
        Console.WriteLine($"Subscription started: {subscriptionStart}");
        Console.WriteLine($"Annual subscription expires: {expiryDate}");

        // With trial period
        var trialPeriod = Period.FromDays(14);
        var trialEnds = subscriptionStart.Add(trialPeriod);
        Console.WriteLine($"14-day trial ends: {trialEnds}");

        // Parsing ISO 8601
        Console.WriteLine("\n--- ISO 8601 Parsing ---");
        var parsedPeriod = Period.Parse("P2Y6M15D");
        Console.WriteLine($"Parsed 'P2Y6M15D': {parsedPeriod}");
        Console.WriteLine($"  Display: {parsedPeriod.ToDisplayString()}");

        // Partial parsing
        Console.WriteLine($"Parsed 'P1Y': {Period.Parse("P1Y")}");
        Console.WriteLine($"Parsed 'P6M': {Period.Parse("P6M")}");
        Console.WriteLine($"Parsed 'P30D': {Period.Parse("P30D")}");

        Console.WriteLine();
    }
}
