using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Ensure.Samples.Samples;

/// <summary>
///     String format guards (Email, URL, Phone, Regex) and numeric guards (Range, Positive, Zero).
/// </summary>
public static class FormatAndNumericSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. Format & Numeric Guards");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowStringFormatGuards();
        ShowNumericGuards();
        ShowStringLengthGuards();

        Console.WriteLine();
    }

    private static void ShowStringFormatGuards()
    {
        Console.WriteLine("  4.1 String format guards — ThrowIfNot*");
        Console.WriteLine("  ------------------------------------------");

        // Valid cases (no exception)
        ThrowIfNotEmail("user@example.com");
        Console.WriteLine("    ThrowIfNotEmail(\"user@example.com\") — OK");

        ThrowIfNotUrl("https://example.com");
        Console.WriteLine("    ThrowIfNotUrl(\"https://example.com\") — OK");

        ThrowIfNotMatch("EVT-2025-001", @"^EVT-\d{4}-\d{3}$");
        Console.WriteLine("    ThrowIfNotMatch(\"EVT-2025-001\", regex) — OK");

        // Invalid case (throws)
        try { ThrowIfNotEmail("not-an-email"); }
        catch (ArgumentException ex) { Console.WriteLine($"    ThrowIfNotEmail(\"not-an-email\") → {ex.GetType().Name}"); }

        Console.WriteLine();
    }

    private static void ShowNumericGuards()
    {
        Console.WriteLine("  4.2 Numeric guards — INumber<T> generic");
        Console.WriteLine("  -------------------------------------------");

        // Works with int, decimal, double, etc.
        ThrowIfNegative(42);
        Console.WriteLine("    ThrowIfNegative(42) — OK");

        ThrowIfNegativeOrZero(1);
        Console.WriteLine("    ThrowIfNegativeOrZero(1) — OK");

        ThrowIfOutOfRange(25, 18, 120);
        Console.WriteLine("    ThrowIfOutOfRange(25, min:18, max:120) — OK");

        try { ThrowIfNegative(-5); }
        catch (ArgumentOutOfRangeException ex) { Console.WriteLine($"    ThrowIfNegative(-5) → {ex.GetType().Name}"); }

        try { ThrowIfOutOfRange(200, 0, 100); }
        catch (ArgumentOutOfRangeException ex) { Console.WriteLine($"    ThrowIfOutOfRange(200, 0, 100) → {ex.GetType().Name}"); }

        Console.WriteLine();
    }

    private static void ShowStringLengthGuards()
    {
        Console.WriteLine("  4.3 String length guards");
        Console.WriteLine("  ---------------------------");

        ThrowIfShorterThan("hello", 3);
        Console.WriteLine("    ThrowIfShorterThan(\"hello\", 3) — OK");

        ThrowIfLongerThan("hi", 10);
        Console.WriteLine("    ThrowIfLongerThan(\"hi\", 10) — OK");

        try { ThrowIfShorterThan("ab", 5); }
        catch (ArgumentException ex) { Console.WriteLine($"    ThrowIfShorterThan(\"ab\", 5) → {ex.GetType().Name}"); }

        Console.WriteLine();
    }
}
