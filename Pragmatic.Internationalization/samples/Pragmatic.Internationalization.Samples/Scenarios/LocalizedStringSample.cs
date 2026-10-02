using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates LocalizedString (entity storage) and TranslationResult (lookup result).
/// </summary>
public static class LocalizedStringSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. TRANSLATIONS & LOCALIZATION");
        Console.WriteLine("   Multi-culture string management");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // LocalizedString type - for entity localization
        Console.WriteLine("LocalizedString type (entity localization):");
        var productName = LocalizedString.From(
            ("en", "Wireless Headphones"),
            ("it", "Cuffie Wireless"),
            ("de", "Kabellose Kopfhörer"),
            ("fr", "Casque Sans Fil")
        );

        I18NContext.SetCulture("en");
        Console.WriteLine($"  en: {productName.Value}");

        I18NContext.SetCulture("it");
        Console.WriteLine($"  it: {productName.Value}");

        I18NContext.SetCulture("de");
        Console.WriteLine($"  de: {productName.Value}");
        Console.WriteLine();

        // Implicit string conversion
        Console.WriteLine("Implicit string conversion:");
        I18NContext.SetCulture("fr");
        string name = productName; // Uses current culture
        Console.WriteLine($"  string name = productName (fr): {name}");
        Console.WriteLine();

        // Fallback behavior
        Console.WriteLine("Fallback behavior:");

        var greeting = LocalizedString.From(
            ("en", "Hello"),
            ("it", "Ciao")
        );

        I18NContext.SetCulture("es"); // Spanish not available
        Console.WriteLine($"  Request 'es' (not available): {greeting.Value}"); // Falls back to 'en'

        I18NContext.SetCulture("it-IT"); // Italian-Italy falls back to 'it'
        Console.WriteLine($"  Request 'it-IT' (falls back to 'it'): {greeting.Value}");
        Console.WriteLine();

        // StringLocalizer - UI string localization
        Console.WriteLine("StringLocalizer (UI localization):");
        var provider = new InMemoryLocalizationProvider()
            .AddString("en", "welcome", "Welcome to our app!")
            .AddString("en", "welcome.user", "Welcome, {0}!")
            .AddString("it", "welcome", "Benvenuto nella nostra app!")
            .AddString("it", "welcome.user", "Benvenuto, {0}!");

        var options = new I18NOptions
        {
            DefaultUICulture = CultureCode.English
        };

        I18NContext.SetCulture("en");
        var localizer = new StringLocalizer(provider, options, "en");
        Console.WriteLine($"  en: {localizer["welcome"]}");
        Console.WriteLine($"  en with arg: {localizer["welcome.user", "John"]}");

        var itLocalizer = localizer.WithCulture("it");
        Console.WriteLine($"  it: {itLocalizer["welcome"]}");
        Console.WriteLine($"  it with arg: {itLocalizer["welcome.user", "Giovanni"]}");
        Console.WriteLine();

        // TranslationResult properties (from StringLocalizer lookup)
        Console.WriteLine("TranslationResult properties:");
        var found = localizer["welcome"];
        var missing = localizer["unknown.key"];

        Console.WriteLine($"  Found key - IsLocalized: {found.IsLocalized}, IsMissing: {found.IsMissing}");
        Console.WriteLine($"  Missing key - IsLocalized: {missing.IsLocalized}, IsMissing: {missing.IsMissing}");
        Console.WriteLine($"  Missing value (returns key): {missing.Value}");
        Console.WriteLine();

        // Reset culture
        I18NContext.SetCulture("en");
    }
}