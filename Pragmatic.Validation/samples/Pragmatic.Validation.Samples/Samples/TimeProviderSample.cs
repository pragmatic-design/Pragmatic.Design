using System.Globalization;

namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     ValidationTimeProvider clock override: date attributes ([FutureDate], [PastDate])
///     read <see cref="ValidationTimeProvider.Current" /> instead of <c>DateTime.UtcNow</c>,
///     so tests (and samples) can pin "now" to a fixed instant for deterministic results.
/// </summary>
public static class TimeProviderSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("10. ValidationTimeProvider — Overriding the Clock");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowDefaultClock();
        ShowOverriddenClock();

        Console.WriteLine();
    }

    private static void ShowDefaultClock()
    {
        Console.WriteLine("  10.1 Default clock (TimeProvider.System)");
        Console.WriteLine("  ------------------------------------------");

        // A date one day ahead of the real wall-clock is in the future.
        var request = new BookSlotRequest { SlotStart = DateTime.UtcNow.AddDays(1) };
        var result = request.Validate();

        Console.WriteLine($"    SlotStart = now + 1 day → IsSuccess: {result.IsSuccess}");
        Console.WriteLine();
    }

    private static void ShowOverriddenClock()
    {
        Console.WriteLine("  10.2 Pinned clock — deterministic [FutureDate] outcome");
        Console.WriteLine("  --------------------------------------------------------");

        // Fixed reference instant: 2030-01-01T00:00:00Z.
        var fixedNow = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Save and restore the process-wide provider (the recommended scope pattern).
        var previous = ValidationTimeProvider.Current;
        ValidationTimeProvider.Current = new FixedTimeProvider(fixedNow);
        try
        {
            // 2029 is BEFORE the pinned "now" → [FutureDate] fails.
            var past = new BookSlotRequest { SlotStart = new DateTime(2029, 6, 1, 0, 0, 0, DateTimeKind.Utc) };
            var pastResult = past.Validate();

            // 2031 is AFTER the pinned "now" → [FutureDate] passes.
            var future = new BookSlotRequest { SlotStart = new DateTime(2031, 6, 1, 0, 0, 0, DateTimeKind.Utc) };
            var futureResult = future.Validate();

            Console.WriteLine($"    Pinned now = {fixedNow.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)}");
            Console.WriteLine($"    SlotStart 2029-06-01 → IsFailure: {pastResult.IsFailure} (before pinned now)");
            Console.WriteLine($"    SlotStart 2031-06-01 → IsSuccess: {futureResult.IsSuccess} (after pinned now)");
        }
        finally
        {
            ValidationTimeProvider.Current = previous;
        }

        Console.WriteLine();
        Console.WriteLine("    Note: always restore Current in a finally block. For parallel");
        Console.WriteLine("    test isolation prefer an AsyncLocal-based scope helper.");
        Console.WriteLine();
    }

    /// <summary>A minimal fixed-instant <see cref="TimeProvider" /> for deterministic date validation.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
