using Pragmatic.Internationalization.Humanizer;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates Humanizer formatters: Quantity, Ordinal, and RelativeTime.
/// </summary>
public static class HumanizerSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. HUMANIZER (QUANTITY, ORDINAL & DURATION)");
        Console.WriteLine("   Human-friendly number and time formatting");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        RunQuantityFormatter();
        RunOrdinalFormatter();
        RunDurationFormatter();
    }

    private static void RunQuantityFormatter()
    {
        Console.WriteLine("QuantityFormatter - Large numbers:");
        Console.WriteLine();

        // English (default)
        Console.WriteLine("  English (en):");
        var enFormatter = new QuantityFormatter("en");
        Console.WriteLine($"    1,500       -> {enFormatter.Format(1500)}");
        Console.WriteLine($"    15,000      -> {enFormatter.Format(15000)}");
        Console.WriteLine($"    1,500,000   -> {enFormatter.Format(1_500_000)}");
        Console.WriteLine($"    2,300,000   -> {enFormatter.Format(2_300_000)}");
        Console.WriteLine($"    1.2 billion -> {enFormatter.Format(1_200_000_000)}");
        Console.WriteLine($"    1.5 trillion-> {enFormatter.Format(1_500_000_000_000)}");
        Console.WriteLine();

        // Italian (uses comma as decimal separator)
        Console.WriteLine("  Italian (it):");
        var itFormatter = new QuantityFormatter("it-IT");
        Console.WriteLine($"    1,500       -> {itFormatter.Format(1500)}");
        Console.WriteLine($"    1,500,000   -> {itFormatter.Format(1_500_000)}");
        Console.WriteLine($"    1.2 billion -> {itFormatter.Format(1_200_000_000)} (Miliardo)");
        Console.WriteLine();

        // German
        Console.WriteLine("  German (de):");
        var deFormatter = new QuantityFormatter("de");
        Console.WriteLine($"    1,000       -> {deFormatter.Format(1000)} (Tausend)");
        Console.WriteLine($"    1,000,000   -> {deFormatter.Format(1_000_000)} (Million)");
        Console.WriteLine($"    1 billion   -> {deFormatter.Format(1_000_000_000)} (Milliarde)");
        Console.WriteLine();

        // Russian
        Console.WriteLine("  Russian (ru):");
        var ruFormatter = new QuantityFormatter("ru");
        Console.WriteLine($"    1,000       -> {ruFormatter.Format(1000)} (тысяча)");
        Console.WriteLine($"    1,000,000   -> {ruFormatter.Format(1_000_000)} (миллион)");
        Console.WriteLine($"    1 billion   -> {ruFormatter.Format(1_000_000_000)} (миллиард)");
        Console.WriteLine();

        // Chinese
        Console.WriteLine("  Chinese (zh):");
        var zhFormatter = new QuantityFormatter("zh");
        Console.WriteLine($"    1,000       -> {zhFormatter.Format(1000)} (千)");
        Console.WriteLine($"    1,000,000   -> {zhFormatter.Format(1_000_000)} (百万)");
        Console.WriteLine();

        // Negative numbers
        Console.WriteLine("  Negative numbers (en):");
        Console.WriteLine($"    -1,500      -> {enFormatter.Format(-1500)}");
        Console.WriteLine($"    -2,300,000  -> {enFormatter.Format(-2_300_000)}");
        Console.WriteLine();

        // Custom suffixes
        Console.WriteLine("  Custom suffixes:");
        var customSuffixes = new QuantitySuffixes("mil", "MM", "bil", "tril");
        var customFormatter = new QuantityFormatter("en", customSuffixes);
        Console.WriteLine($"    1,500 (custom) -> {customFormatter.Format(1500)}");
        Console.WriteLine($"    1,500,000 (custom) -> {customFormatter.Format(1_500_000)}");
        Console.WriteLine();
    }

    private static void RunOrdinalFormatter()
    {
        Console.WriteLine("OrdinalFormatter - Ordinal numbers:");
        Console.WriteLine();

        // English
        Console.WriteLine("  English (en):");
        var enFormatter = new OrdinalFormatter("en");
        Console.WriteLine($"    1  -> {enFormatter.Format(1)}");
        Console.WriteLine($"    2  -> {enFormatter.Format(2)}");
        Console.WriteLine($"    3  -> {enFormatter.Format(3)}");
        Console.WriteLine($"    4  -> {enFormatter.Format(4)}");
        Console.WriteLine($"    11 -> {enFormatter.Format(11)} (special case)");
        Console.WriteLine($"    12 -> {enFormatter.Format(12)} (special case)");
        Console.WriteLine($"    13 -> {enFormatter.Format(13)} (special case)");
        Console.WriteLine($"    21 -> {enFormatter.Format(21)}");
        Console.WriteLine($"    22 -> {enFormatter.Format(22)}");
        Console.WriteLine($"    23 -> {enFormatter.Format(23)}");
        Console.WriteLine();

        // Italian
        Console.WriteLine("  Italian (it):");
        var itFormatter = new OrdinalFormatter("it");
        Console.WriteLine($"    1 -> {itFormatter.Format(1)}");
        Console.WriteLine($"    2 -> {itFormatter.Format(2)}");
        Console.WriteLine($"    5 -> {itFormatter.Format(5)}");
        Console.WriteLine();

        // German
        Console.WriteLine("  German (de):");
        var deFormatter = new OrdinalFormatter("de");
        Console.WriteLine($"    1 -> {deFormatter.Format(1)}");
        Console.WriteLine($"    2 -> {deFormatter.Format(2)}");
        Console.WriteLine($"    5 -> {deFormatter.Format(5)}");
        Console.WriteLine();

        // French (with gender)
        Console.WriteLine("  French (fr) - with gender:");
        var frMasculine = new OrdinalFormatter("fr");
        var frFeminine = new OrdinalFormatter("fr", OrdinalGender.Feminine);
        Console.WriteLine($"    1 (masculine) -> {frMasculine.Format(1)}");
        Console.WriteLine($"    1 (feminine)  -> {frFeminine.Format(1)}");
        Console.WriteLine($"    2 (any)       -> {frMasculine.Format(2)}");
        Console.WriteLine();

        // Russian
        Console.WriteLine("  Russian (ru):");
        var ruFormatter = new OrdinalFormatter("ru");
        Console.WriteLine($"    1 -> {ruFormatter.Format(1)}");
        Console.WriteLine($"    5 -> {ruFormatter.Format(5)}");
        Console.WriteLine();

        // Chinese
        Console.WriteLine("  Chinese (zh):");
        var zhFormatter = new OrdinalFormatter("zh");
        Console.WriteLine($"    1 -> {zhFormatter.Format(1)}");
        Console.WriteLine($"    5 -> {zhFormatter.Format(5)}");
        Console.WriteLine();

        // Japanese
        Console.WriteLine("  Japanese (ja):");
        var jaFormatter = new OrdinalFormatter("ja");
        Console.WriteLine($"    1 -> {jaFormatter.Format(1)}");
        Console.WriteLine($"    5 -> {jaFormatter.Format(5)}");
        Console.WriteLine();

        // Korean
        Console.WriteLine("  Korean (ko):");
        var koFormatter = new OrdinalFormatter("ko");
        Console.WriteLine($"    1 -> {koFormatter.Format(1)}");
        Console.WriteLine($"    5 -> {koFormatter.Format(5)}");
        Console.WriteLine();
    }

    private static void RunDurationFormatter()
    {
        Console.WriteLine("DurationFormatter - Time durations:");
        Console.WriteLine();

        // Short format (English)
        Console.WriteLine("  Short format (en):");
        var enShort = new DurationFormatter("en");
        Console.WriteLine($"    30 seconds      -> {enShort.Format(TimeSpan.FromSeconds(30))}");
        Console.WriteLine($"    5 minutes       -> {enShort.Format(TimeSpan.FromMinutes(5))}");
        Console.WriteLine($"    2h 30m          -> {enShort.Format(TimeSpan.FromMinutes(150))}");
        Console.WriteLine($"    1 day 5 hours   -> {enShort.Format(new TimeSpan(1, 5, 30, 0))}");
        Console.WriteLine($"    Negative        -> {enShort.Format(TimeSpan.FromMinutes(-90))}");
        Console.WriteLine();

        // Long format (English)
        Console.WriteLine("  Long format (en):");
        var enLong = new DurationFormatter("en", DurationFormat.Long);
        Console.WriteLine($"    1 minute        -> {enLong.Format(TimeSpan.FromMinutes(1))}");
        Console.WriteLine($"    5 minutes       -> {enLong.Format(TimeSpan.FromMinutes(5))}");
        Console.WriteLine($"    1 hour 30 min   -> {enLong.Format(TimeSpan.FromMinutes(90))}");
        Console.WriteLine($"    2 hours 1 min   -> {enLong.Format(new TimeSpan(0, 2, 1, 0))}");
        Console.WriteLine();

        // Compact format (clock-style)
        Console.WriteLine("  Compact format (en):");
        var enCompact = new DurationFormatter("en", DurationFormat.Compact);
        Console.WriteLine($"    5:30            -> {enCompact.Format(new TimeSpan(0, 0, 5, 30))}");
        Console.WriteLine($"    2:30:00         -> {enCompact.Format(TimeSpan.FromMinutes(150))}");
        Console.WriteLine($"    1:02:30:45      -> {enCompact.Format(new TimeSpan(1, 2, 30, 45))}");
        Console.WriteLine();

        // Multi-language short format
        Console.WriteLine("  Short format - different cultures (2h 30m):");
        var duration = TimeSpan.FromMinutes(150);
        Console.WriteLine($"    Italian (it)    -> {new DurationFormatter("it").Format(duration)}");
        Console.WriteLine($"    German (de)     -> {new DurationFormatter("de").Format(duration)}");
        Console.WriteLine($"    French (fr)     -> {new DurationFormatter("fr").Format(duration)}");
        Console.WriteLine($"    Spanish (es)    -> {new DurationFormatter("es").Format(duration)}");
        Console.WriteLine($"    Russian (ru)    -> {new DurationFormatter("ru").Format(duration)}");
        Console.WriteLine($"    Chinese (zh)    -> {new DurationFormatter("zh").Format(duration)}");
        Console.WriteLine($"    Japanese (ja)   -> {new DurationFormatter("ja").Format(duration)}");
        Console.WriteLine();

        // Multi-language long format
        Console.WriteLine("  Long format - different cultures (2h 30m):");
        Console.WriteLine(
            $"    Italian (it)    -> {new DurationFormatter("it", DurationFormat.Long).Format(duration)}");
        Console.WriteLine(
            $"    German (de)     -> {new DurationFormatter("de", DurationFormat.Long).Format(duration)}");
        Console.WriteLine(
            $"    French (fr)     -> {new DurationFormatter("fr", DurationFormat.Long).Format(duration)}");
        Console.WriteLine(
            $"    Spanish (es)    -> {new DurationFormatter("es", DurationFormat.Long).Format(duration)}");
        Console.WriteLine();

        // MaxParts control
        Console.WriteLine("  MaxParts control (1d 5h 30m 45s):");
        var fullDuration = new TimeSpan(1, 5, 30, 45);
        Console.WriteLine(
            $"    maxParts=1      -> {new DurationFormatter("en", DurationFormat.Short, 1).Format(fullDuration)}");
        Console.WriteLine($"    maxParts=2      -> {new DurationFormatter("en").Format(fullDuration)}");
        Console.WriteLine(
            $"    maxParts=3      -> {new DurationFormatter("en", DurationFormat.Short, 3).Format(fullDuration)}");
        Console.WriteLine(
            $"    maxParts=4      -> {new DurationFormatter("en", DurationFormat.Short, 4).Format(fullDuration)}");
        Console.WriteLine();

        // ShowZeroParts
        Console.WriteLine("  ShowZeroParts (1d 0h 30m):");
        var gappedDuration = new TimeSpan(1, 0, 30, 0);
        Console.WriteLine(
            $"    showZero=false  -> {new DurationFormatter("en", DurationFormat.Short, 3).Format(gappedDuration)}");
        Console.WriteLine(
            $"    showZero=true   -> {new DurationFormatter("en", DurationFormat.Short, 3, true).Format(gappedDuration)}");
        Console.WriteLine();

        // Static factory methods
        Console.WriteLine("  Static factory methods:");
        Console.WriteLine($"    Short(\"en\")     -> {DurationFormatter.Short("en").Format(duration)}");
        Console.WriteLine($"    Long(\"en\")      -> {DurationFormatter.Long("en").Format(duration)}");
        Console.WriteLine($"    Compact(\"en\")   -> {DurationFormatter.Compact("en").Format(duration)}");
        Console.WriteLine();
    }
}