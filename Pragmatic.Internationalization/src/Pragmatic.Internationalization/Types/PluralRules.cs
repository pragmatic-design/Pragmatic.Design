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
        // European Portuguese is the one region with a rule of its own in CLDR (pt_PT): Italian's, not Brazil's.
        if (string.Equals(culture, "pt-PT", StringComparison.OrdinalIgnoreCase))
            return GetItalianCategory(count);

        // Normalize culture to base language code
        var lang = GetLanguageCode(culture);

        return lang switch
        {
            // No plurals (Japanese, Chinese, Korean, Thai, Indonesian, Malay)
            "ja" or "zh" or "ko" or "th" or "id" or "ms" => PluralCategory.Other,

            // Romance languages: French and Portuguese put 0 in the singular, Italian, Spanish and Catalan do not
            "fr" or "pt" => GetFrenchCategory(count),
            "it" or "es" or "ca" => GetItalianCategory(count),

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

            // Turkish, Azerbaijani, Georgian, Kyrgyz, Kazakh, Uzbek: one (1), other
            "tr" or "az" or "ka" or "ky" or "kk" or "uz" => GetGermanicCategory(count),

            // Finnish, Estonian
            "fi" or "et" => GetGermanicCategory(count),

            // Hungarian
            "hu" => GetGermanicCategory(count),

            // Greek
            "el" => GetGermanicCategory(count),

            // Hindi, Bengali, Gujarati, Kannada, Punjabi and Vietnamese: one (0, 1), other
            "hi" or "bn" or "gu" or "kn" or "pa" or "vi" => GetIndicCategory(count),

            // Malayalam, Marathi, Tamil, Telugu: one (1), other
            "ml" or "mr" or "ta" or "te" => GetGermanicCategory(count),

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