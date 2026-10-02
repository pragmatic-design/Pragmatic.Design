using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates CLDR-compliant plural rules for multiple languages.
/// </summary>
public static class PluralRulesSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. PLURAL RULES (CLDR)");
        Console.WriteLine("   Language-specific pluralization");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Basic plural rules
        Console.WriteLine("Basic plural categories:");
        Console.WriteLine("  English (en):");
        Console.WriteLine($"    0 -> {PluralRules.GetCategory("en", 0)}");
        Console.WriteLine($"    1 -> {PluralRules.GetCategory("en", 1)}");
        Console.WriteLine($"    2 -> {PluralRules.GetCategory("en", 2)}");
        Console.WriteLine($"    5 -> {PluralRules.GetCategory("en", 5)}");
        Console.WriteLine();

        // Romance languages (French, Italian - 0 and 1 are singular)
        Console.WriteLine("  French (fr) - 0 and 1 are singular:");
        Console.WriteLine($"    0 -> {PluralRules.GetCategory("fr", 0)}");
        Console.WriteLine($"    1 -> {PluralRules.GetCategory("fr", 1)}");
        Console.WriteLine($"    2 -> {PluralRules.GetCategory("fr", 2)}");
        Console.WriteLine();

        // Slavic languages (Russian - complex rules)
        Console.WriteLine("  Russian (ru) - complex rules:");
        Console.WriteLine($"    1 -> {PluralRules.GetCategory("ru", 1)} (1 элемент)");
        Console.WriteLine($"    2 -> {PluralRules.GetCategory("ru", 2)} (2 элемента)");
        Console.WriteLine($"    5 -> {PluralRules.GetCategory("ru", 5)} (5 элементов)");
        Console.WriteLine($"    11 -> {PluralRules.GetCategory("ru", 11)} (11 элементов)");
        Console.WriteLine($"    21 -> {PluralRules.GetCategory("ru", 21)} (21 элемент)");
        Console.WriteLine($"    22 -> {PluralRules.GetCategory("ru", 22)} (22 элемента)");
        Console.WriteLine();

        // Arabic (6 forms)
        Console.WriteLine("  Arabic (ar) - 6 plural forms:");
        Console.WriteLine($"    0 -> {PluralRules.GetCategory("ar", 0)}");
        Console.WriteLine($"    1 -> {PluralRules.GetCategory("ar", 1)}");
        Console.WriteLine($"    2 -> {PluralRules.GetCategory("ar", 2)}");
        Console.WriteLine($"    5 -> {PluralRules.GetCategory("ar", 5)}");
        Console.WriteLine($"    15 -> {PluralRules.GetCategory("ar", 15)}");
        Console.WriteLine($"    100 -> {PluralRules.GetCategory("ar", 100)}");
        Console.WriteLine();

        // East Asian (no plural forms)
        Console.WriteLine("  Japanese (ja) - no plural forms:");
        Console.WriteLine($"    1 -> {PluralRules.GetCategory("ja", 1)}");
        Console.WriteLine($"    5 -> {PluralRules.GetCategory("ja", 5)}");
        Console.WriteLine($"    100 -> {PluralRules.GetCategory("ja", 100)}");
        Console.WriteLine();

        // Using plurals with StringLocalizer
        Console.WriteLine("Plurals with StringLocalizer:");
        var provider = new InMemoryLocalizationProvider()
            .AddPlural("en", "items",
                (PluralCategory.One, "{count} item"),
                (PluralCategory.Other, "{count} items"))
            .AddPlural("ru", "items",
                (PluralCategory.One, "{count} элемент"),
                (PluralCategory.Few, "{count} элемента"),
                (PluralCategory.Many, "{count} элементов"))
            .AddPlural("ar", "items",
                (PluralCategory.Zero, "لا عناصر"),
                (PluralCategory.One, "عنصر واحد"),
                (PluralCategory.Two, "عنصران"),
                (PluralCategory.Few, "{count} عناصر"),
                (PluralCategory.Many, "{count} عنصرًا"),
                (PluralCategory.Other, "{count} عنصر"));

        var options = new I18NOptions { DefaultUICulture = CultureCode.English };

        var enLocalizer = new StringLocalizer(provider, options, "en");
        Console.WriteLine("  English:");
        Console.WriteLine($"    1: {enLocalizer.Plural("items", 1)}");
        Console.WriteLine($"    5: {enLocalizer.Plural("items", 5)}");

        var ruLocalizer = new StringLocalizer(provider, options, "ru");
        Console.WriteLine("  Russian:");
        Console.WriteLine($"    1: {ruLocalizer.Plural("items", 1)}");
        Console.WriteLine($"    2: {ruLocalizer.Plural("items", 2)}");
        Console.WriteLine($"    5: {ruLocalizer.Plural("items", 5)}");
        Console.WriteLine();
    }
}