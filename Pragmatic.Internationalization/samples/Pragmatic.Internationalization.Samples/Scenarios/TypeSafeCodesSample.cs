using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates type-safe ISO codes: LanguageCode, CountryCode, CultureCode.
/// </summary>
public static class TypeSafeCodesSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("TYPE-SAFE ISO CODES");
        Console.WriteLine("   LanguageCode, CountryCode, CultureCode - compile-time safety");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // LanguageCode - ISO 639-1 (Generated from official data)
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("LanguageCode (ISO 639-1):");
        Console.WriteLine($"  English:  Code={LanguageCode.English.Code}, Name={LanguageCode.English.Name}");
        Console.WriteLine($"  Italian:  Code={LanguageCode.Italian.Code}, NativeName={LanguageCode.Italian.NativeName}");
        Console.WriteLine($"  German:   Code={LanguageCode.German.Code}");
        Console.WriteLine($"  Japanese: Code={LanguageCode.Japanese.Code}");
        Console.WriteLine($"  Arabic:   Code={LanguageCode.Arabic.Code}, RTL={LanguageCode.Arabic.IsRightToLeft}");
        Console.WriteLine();

        // Parsing from strings (user input, HTTP headers, DB)
        Console.WriteLine("Parsing from strings:");
        if (LanguageCode.TryFromCode("it", out var italian))
        {
            Console.WriteLine($"  TryFromCode('it') → {italian.Name}");
        }
        if (LanguageCode.TryFromCode("invalid", out _) == false)
        {
            Console.WriteLine($"  TryFromCode('invalid') → false (safe!)");
        }
        Console.WriteLine();

        // Plural rule families
        Console.WriteLine("Plural Rule Families:");
        Console.WriteLine($"  English:  {LanguageCode.English.PluralFamily} (One/Other)");
        Console.WriteLine($"  Russian:  {LanguageCode.Russian.PluralFamily} (One/Few/Many/Other)");
        Console.WriteLine($"  Japanese: {LanguageCode.Japanese.PluralFamily} (Other only)");
        Console.WriteLine($"  Arabic:   {LanguageCode.Arabic.PluralFamily} (Zero/One/Two/Few/Many/Other)");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // CountryCode - ISO 3166-1 (Generated from official data)
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("CountryCode (ISO 3166-1):");
        Console.WriteLine($"  US: {CountryCode.US.Name}, Currency={CountryCode.US.DefaultCurrency.Code}");
        Console.WriteLine($"  Italy: {CountryCode.Italy.Name}, Currency={CountryCode.Italy.DefaultCurrency.Code}");
        Console.WriteLine($"  Switzerland: {CountryCode.Switzerland.Name}, Currency={CountryCode.Switzerland.DefaultCurrency.Code}");
        Console.WriteLine($"  UK: {CountryCode.UK.Name}, DateFormat={CountryCode.UK.DateFormat}");
        Console.WriteLine();

        // Country-Language-Currency integration
        Console.WriteLine("Country → Language → Currency Integration:");
        var country = CountryCode.Italy;
        Console.WriteLine($"  {country.Name}:");
        Console.WriteLine($"    Default Language: {country.DefaultLanguage.Name}");
        Console.WriteLine($"    Default Currency: {country.DefaultCurrency.Code} ({country.DefaultCurrency.Name})");
        Console.WriteLine($"    Date Format: {country.DateFormat}");
        Console.WriteLine($"    Decimal Separator: '{country.NumberDecimalSeparator}'");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // CultureCode - Composite (Language + optional Country)
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("CultureCode (Language + optional Country):");

        // Language-only cultures (for simple apps)
        Console.WriteLine("  Language-only cultures:");
        Console.WriteLine($"    CultureCode.Italian: {CultureCode.Italian.Code}");
        Console.WriteLine($"    CultureCode.English: {CultureCode.English.Code}");
        Console.WriteLine($"    CultureCode.German: {CultureCode.German.Code}");
        Console.WriteLine();

        // Language+Country cultures (for e-commerce, finance)
        Console.WriteLine("  Language+Country cultures:");
        Console.WriteLine($"    CultureCode.EnglishUS: {CultureCode.EnglishUS.Code}");
        Console.WriteLine($"    CultureCode.EnglishUK: {CultureCode.EnglishUK.Code}");
        Console.WriteLine($"    CultureCode.GermanSwitzerland: {CultureCode.GermanSwitzerland.Code}");
        Console.WriteLine($"    CultureCode.FrenchCanada: {CultureCode.FrenchCanada.Code}");
        Console.WriteLine();

        // Derived properties
        Console.WriteLine("Derived Properties:");
        var culture = CultureCode.EnglishUK;
        Console.WriteLine($"  {culture.Code}:");
        Console.WriteLine($"    Language: {culture.Language.Name}");
        Console.WriteLine($"    Country: {culture.Country?.Name ?? "none"}");
        Console.WriteLine($"    Currency: {culture.DefaultCurrency.Code}");
        Console.WriteLine($"    RTL: {culture.IsRightToLeft}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Parsing and Conversion
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Parsing from External Input:");

        // From Accept-Language header
        var acceptLanguage = "it-IT, en;q=0.9, de;q=0.8";
        var best = CultureCode.FromAcceptLanguage(acceptLanguage);
        Console.WriteLine($"  Accept-Language: '{acceptLanguage}'");
        Console.WriteLine($"  Best match: {best?.Code ?? "none"}");
        Console.WriteLine();

        // From string (database, config files)
        if (CultureCode.TryFromString("de-CH", out var parsed))
        {
            Console.WriteLine($"  TryFromString('de-CH') → {parsed.Code}");
            Console.WriteLine($"    Language: {parsed.Language.Name}");
            Console.WriteLine($"    Country: {parsed.Country?.Name}");
        }
        Console.WriteLine();

        // To .NET CultureInfo
        var cultureInfo = CultureCode.Italian.ToCultureInfo();
        Console.WriteLine("Conversion to .NET CultureInfo:");
        Console.WriteLine($"  CultureCode.Italian.ToCultureInfo() → {cultureInfo.Name}");
        Console.WriteLine($"    DisplayName: {cultureInfo.DisplayName}");
        Console.WriteLine();

        // ═══════════════════════════════════════════════════════════════
        // Implicit Conversions
        // ═══════════════════════════════════════════════════════════════
        Console.WriteLine("Implicit Conversions (for ergonomics):");

        // Culture → string
        string cultureString = CultureCode.Italian;
        Console.WriteLine($"  string s = CultureCode.Italian → \"{cultureString}\"");

        // Language → string
        string langString = LanguageCode.English;
        Console.WriteLine($"  string s = LanguageCode.English → \"{langString}\"");

        // Country → string
        string countryString = CountryCode.US;
        Console.WriteLine($"  string s = CountryCode.US → \"{countryString}\"");
        Console.WriteLine();
    }
}
