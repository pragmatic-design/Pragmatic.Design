namespace Pragmatic.Internationalization.Types;

/// <summary>
///     CLDR plural rules for determining the correct plural category for a number.
///     Based on Unicode CLDR Plural Rules: https://cldr.unicode.org/index/cldr-spec/plural-rules
/// </summary>
/// <remarks>
///     This class is split into partial files by language family:
///     <list type="bullet">
///         <item>PluralRules.cs - Core logic and language code extraction</item>
///         <item>PluralRules.Germanic.cs - English, German, Dutch, etc.</item>
///         <item>PluralRules.Romance.cs - French, Italian, Spanish, etc.</item>
///         <item>PluralRules.Slavic.cs - Russian, Polish, Czech, etc.</item>
///         <item>PluralRules.Other.cs - Arabic, Welsh, Irish, Baltic, etc.</item>
///     </list>
/// </remarks>
public static partial class PluralRules
{
    /// <summary>
    ///     Gets the plural category for the given count in the specified culture.
    /// </summary>
    public static PluralCategory GetCategory(string culture, int count)
    {
        // Normalize culture to base language code
        var lang = GetLanguageCode(culture);

        return lang switch
        {
            // East Asian languages: no plurals (Japanese, Chinese, Korean, Vietnamese, etc.)
            "ja" or "zh" or "ko" or "vi" or "th" or "id" or "ms" => PluralCategory.Other,

            // Romance languages: singular/plural (French, Italian, Spanish, Portuguese, etc.)
            "fr" or "it" or "es" or "pt" or "ca" => GetRomanceCategory(count),

            // Germanic languages: singular/plural (English, German, Dutch, Swedish, etc.)
            "en" or "de" or "nl" or "sv" or "da" or "no" or "nb" or "nn" => GetGermanicCategory(count),

            // Slavic languages with complex rules (Russian, Ukrainian, Polish, Czech, etc.)
            "ru" or "uk" or "be" => GetEastSlavicCategory(count),
            "pl" => GetPolishCategory(count),
            "cs" or "sk" => GetCzechSlovakCategory(count),
            "hr" or "sr" or "bs" => GetSerboCroatianCategory(count),

            // Arabic: complex plural system (zero, one, two, few, many, other)
            "ar" => GetArabicCategory(count),

            // Welsh: zero, one, two, few, many, other
            "cy" => GetWelshCategory(count),

            // Irish: complex system
            "ga" => GetIrishCategory(count),

            // Latvian
            "lv" => GetLatvianCategory(count),

            // Lithuanian
            "lt" => GetLithuanianCategory(count),

            // Slovenian: singular, dual, few, other
            "sl" => GetSlovenianCategory(count),

            // Romanian
            "ro" => GetRomanianCategory(count),

            // Hebrew
            "he" or "iw" => GetHebrewCategory(count),

            // Turkish and similar: only 'other'
            "tr" or "az" or "ka" or "ky" or "kk" or "uz" => PluralCategory.Other,

            // Finnish, Estonian
            "fi" or "et" => GetGermanicCategory(count),

            // Hungarian
            "hu" => GetGermanicCategory(count),

            // Greek
            "el" => GetGermanicCategory(count),

            // Hindi and related
            "hi" or "bn" or "gu" or "kn" or "ml" or "mr" or "pa" or "ta" or "te" => GetIndicCategory(count),

            // Default: use English-style singular/plural
            _ => GetGermanicCategory(count)
        };
    }

    /// <summary>
    ///     Gets the language code from a full culture code (e.g., "en-US" → "en").
    /// </summary>
    private static string GetLanguageCode(string culture)
    {
        var dashIndex = culture.IndexOf('-');
        return dashIndex > 0 ? culture[..dashIndex].ToLowerInvariant() : culture.ToLowerInvariant();
    }
}