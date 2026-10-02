using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Pragmatic.Internationalization.Validation;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates CultureValidation helpers for user input validation.
/// </summary>
public static class ValidationHelpersSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("VALIDATION HELPERS");
        Console.WriteLine("   Safe parsing and validation of user input");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Setup: Create a resolver with supported cultures
        // ═══════════════════════════════════════════════════════════════
        var resolver = CreateResolver([
            CultureCode.Italian,
            CultureCode.English,
            CultureCode.German,
            CultureCode.EnglishUS
        ]);

        // ═══════════════════════════════════════════════════════════════
        // TryValidate - Basic Culture Validation
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("TryValidate (Basic Culture Validation):");

        // Valid culture
        ValidateAndPrint("en-US");
        ValidateAndPrint("it");

        // Invalid cultures
        ValidateAndPrint("");
        ValidateAndPrint("invalid");
        ValidateAndPrint("xx-YY");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // TryValidateSupported - Culture Must Be in Supported List
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("TryValidateSupported (Against Supported List):");
        Console.WriteLine($"  Supported: [it, en, de, en-US]");
        Console.WriteLine();

        ValidateSupportedAndPrint("it", resolver);      // Supported
        ValidateSupportedAndPrint("en-US", resolver);   // Supported
        ValidateSupportedAndPrint("fr", resolver);      // Valid but NOT supported
        ValidateSupportedAndPrint("invalid", resolver); // Invalid
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // TryValidateLanguage - ISO 639-1 Language Codes
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("TryValidateLanguage (ISO 639-1):");

        ValidateLanguageAndPrint("en");
        ValidateLanguageAndPrint("it");
        ValidateLanguageAndPrint("EN");  // Case insensitive
        ValidateLanguageAndPrint("xx");  // Invalid
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // TryValidateCountry - ISO 3166-1 Country Codes
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("TryValidateCountry (ISO 3166-1):");

        ValidateCountryAndPrint("US");
        ValidateCountryAndPrint("IT");
        ValidateCountryAndPrint("us");  // Case insensitive
        ValidateCountryAndPrint("XX");  // Invalid
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // TryValidateCurrency - ISO 4217 Currency Codes
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("TryValidateCurrency (ISO 4217):");

        ValidateCurrencyAndPrint("USD");
        ValidateCurrencyAndPrint("EUR");
        ValidateCurrencyAndPrint("eur");  // Case insensitive
        ValidateCurrencyAndPrint("XXX");  // Invalid
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // GetBestMatch - From List of Candidates
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("GetBestMatch (From Candidate List):");

        var candidates1 = new[] { "fr", "es", "it" }; // fr and es not supported, it is
        var best1 = CultureValidation.GetBestMatch(candidates1, resolver);
        Console.WriteLine($"  Candidates: [fr, es, it] → Best match: {best1?.Code ?? "none"}");

        var candidates2 = new[] { "fr", "es", "pt" }; // None supported
        var best2 = CultureValidation.GetBestMatch(candidates2, resolver);
        Console.WriteLine($"  Candidates: [fr, es, pt] → Best match: {best2?.Code ?? "none"}");

        var candidates3 = new[] { "invalid", "xx", "de" }; // Skip invalid, find de
        var best3 = CultureValidation.GetBestMatch(candidates3, resolver);
        Console.WriteLine($"  Candidates: [invalid, xx, de] → Best match: {best3?.Code ?? "none"}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // GetBestMatchFromAcceptLanguage - HTTP Accept-Language Header
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("GetBestMatchFromAcceptLanguage (HTTP Header):");

        TestAcceptLanguage("it-IT, en;q=0.9", resolver);
        TestAcceptLanguage("en-US, en;q=0.9, it;q=0.8", resolver);
        TestAcceptLanguage("fr-FR, es;q=0.9", resolver);  // None directly supported
        TestAcceptLanguage("de-CH, de;q=0.9", resolver);  // de-CH not supported, but de is
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Real-World Usage: Controller Action
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Real-World Usage (API Endpoint):");
        Console.WriteLine();
        Console.WriteLine("  // POST /api/preferences?lang=it");
        Console.WriteLine("  [HttpPost]");
        Console.WriteLine("  public IActionResult SetLanguage([FromQuery] string lang)");
        Console.WriteLine("  {");
        Console.WriteLine("      if (!CultureValidation.TryValidateSupported(lang, _resolver, out var culture, out var error))");
        Console.WriteLine("          return BadRequest(error);");
        Console.WriteLine("      ");
        Console.WriteLine("      I18N.SetCulture(culture);");
        Console.WriteLine("      return Ok();");
        Console.WriteLine("  }");
        Console.WriteLine();

        // Simulate the validation
        SimulateEndpoint("it", resolver);
        SimulateEndpoint("fr", resolver);
        SimulateEndpoint("invalid", resolver);
        Console.WriteLine();
    }

    private static void ValidateAndPrint(string? input)
    {
        if (CultureValidation.TryValidate(input, out var culture, out var error))
        {
            Console.WriteLine($"  '{input}' → ✓ Valid: {culture.Code}");
        }
        else
        {
            Console.WriteLine($"  '{input}' → ✗ {error}");
        }
    }

    private static void ValidateSupportedAndPrint(string? input, I18NConfigResolver resolver)
    {
        if (CultureValidation.TryValidateSupported(input, resolver, out var culture, out var error))
        {
            Console.WriteLine($"  '{input}' → ✓ Supported: {culture.Code}");
        }
        else
        {
            Console.WriteLine($"  '{input}' → ✗ {error}");
        }
    }

    private static void ValidateLanguageAndPrint(string? input)
    {
        if (CultureValidation.TryValidateLanguage(input, out var lang, out var error))
        {
            Console.WriteLine($"  '{input}' → ✓ Valid: {lang.Code} ({lang.Name})");
        }
        else
        {
            Console.WriteLine($"  '{input}' → ✗ {error}");
        }
    }

    private static void ValidateCountryAndPrint(string? input)
    {
        if (CultureValidation.TryValidateCountry(input, out var country, out var error))
        {
            Console.WriteLine($"  '{input}' → ✓ Valid: {country.Code} ({country.Name})");
        }
        else
        {
            Console.WriteLine($"  '{input}' → ✗ {error}");
        }
    }

    private static void ValidateCurrencyAndPrint(string? input)
    {
        if (CultureValidation.TryValidateCurrency(input, out var currency, out var error))
        {
            Console.WriteLine($"  '{input}' → ✓ Valid: {currency.Code} ({currency.Name})");
        }
        else
        {
            Console.WriteLine($"  '{input}' → ✗ {error}");
        }
    }

    private static void TestAcceptLanguage(string header, I18NConfigResolver resolver)
    {
        var best = CultureValidation.GetBestMatchFromAcceptLanguage(header, resolver);
        Console.WriteLine($"  '{header}'");
        Console.WriteLine($"    → {best?.Code ?? "no match found"}");
    }

    private static void SimulateEndpoint(string lang, I18NConfigResolver resolver)
    {
        Console.Write($"  POST /api/preferences?lang={lang} → ");
        if (CultureValidation.TryValidateSupported(lang, resolver, out _, out var error))
        {
            Console.WriteLine("200 OK");
        }
        else
        {
            Console.WriteLine($"400 BadRequest: {error}");
        }
    }

    private static I18NConfigResolver CreateResolver(IReadOnlyList<CultureCode> supported)
    {
        return new I18NConfigResolver([
            new TestConfigProvider
            {
                Priority = 0,
                Config = new I18NConfig
                {
                    DefaultUICulture = supported[0],
                    SupportedCultures = supported
                }
            }
        ]);
    }

    private sealed class TestConfigProvider : II18NConfigProvider
    {
        public int Priority { get; init; }
        public I18NConfig? Config { get; init; }
        public I18NConfig? GetConfiguration() => Config;
    }
}
