using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Samples.Samples;

/// <summary>
///     Demonstrates DST (Daylight Saving Time) edge-case handling:
///     <list type="bullet">
///         <item><see cref="NonExistentTimePolicy" /> — the spring-forward gap (a wall-clock time that never happens).</item>
///         <item><see cref="AmbiguousTimePolicy" /> — the fall-back overlap (a wall-clock time that happens twice).</item>
///     </list>
///     Uses Europe/Rome 2024 transitions: spring forward 2024-03-31 02:00→03:00, fall back 2024-10-27 03:00→02:00.
/// </summary>
public static class DstSample
{
    public static void Run()
    {
        Console.WriteLine("--- DST Handling Sample ---\n");

        var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");

        // ------------------------------------------------------------------
        // Spring forward: 02:30 on 2024-03-31 does NOT exist in Rome.
        // ------------------------------------------------------------------
        var nonExistentLocal = new DateTime(2024, 3, 31, 2, 30, 0);
        Console.WriteLine($"Spring-forward gap: {nonExistentLocal:yyyy-MM-dd HH:mm} (does not exist in Rome)\n");

        // ShiftForward (default): jump to the first valid instant after the gap (03:00).
        var shifted = ZonedDateTime.FromLocal(nonExistentLocal, rome, NonExistentTimePolicy.ShiftForward);
        Console.WriteLine($"  ShiftForward  -> {shifted}");

        // ThrowException: force the caller to handle the impossible time.
        try
        {
            ZonedDateTime.FromLocal(nonExistentLocal, rome, NonExistentTimePolicy.ThrowException);
        }
        catch (NonExistentTimeException ex)
        {
            Console.WriteLine($"  ThrowException -> caught NonExistentTimeException: {ex.Message.Split('.')[0]}");
        }

        // ------------------------------------------------------------------
        // Fall back: 02:30 on 2024-10-27 exists TWICE in Rome
        // (once at +02:00 daylight, once at +01:00 standard).
        // ------------------------------------------------------------------
        var ambiguousLocal = new DateTime(2024, 10, 27, 2, 30, 0);
        Console.WriteLine($"\nFall-back overlap: {ambiguousLocal:yyyy-MM-dd HH:mm} (occurs twice in Rome)\n");

        // UseStandardTime (default): the later occurrence, +01:00.
        var standard = ZonedDateTime.FromLocal(
            ambiguousLocal, rome,
            ambiguousPolicy: AmbiguousTimePolicy.UseStandardTime);
        Console.WriteLine($"  UseStandardTime -> {standard} (offset {standard.Offset})");

        // UseDaylightTime: the earlier occurrence, +02:00.
        var daylight = ZonedDateTime.FromLocal(
            ambiguousLocal, rome,
            ambiguousPolicy: AmbiguousTimePolicy.UseDaylightTime);
        Console.WriteLine($"  UseDaylightTime -> {daylight} (offset {daylight.Offset})");

        // The two interpretations differ by exactly one hour in absolute (UTC) time.
        var gap = standard.ToUtc() - daylight.ToUtc();
        Console.WriteLine($"  UTC difference between the two interpretations: {gap}");

        // ThrowException: force the caller to disambiguate.
        try
        {
            ZonedDateTime.FromLocal(ambiguousLocal, rome,
                ambiguousPolicy: AmbiguousTimePolicy.ThrowException);
        }
        catch (AmbiguousTimeException ex)
        {
            Console.WriteLine($"  ThrowException  -> caught AmbiguousTimeException: {ex.Message.Split('.')[0]}");
        }

        // ------------------------------------------------------------------
        // FromLocalStrict is the "fail loud" shortcut: throws on either edge case.
        // ------------------------------------------------------------------
        Console.WriteLine("\nFromLocalStrict (throws on both gap and overlap):");
        var safeLocal = new DateTime(2024, 6, 15, 12, 0, 0); // an ordinary, unambiguous summer time
        var strict = ZonedDateTime.FromLocalStrict(safeLocal, rome);
        Console.WriteLine($"  Unambiguous time accepted -> {strict}");

        Console.WriteLine();
    }
}
