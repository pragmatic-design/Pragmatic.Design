using Pragmatic.Internationalization.Context;

namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Formats numbers as ordinals (e.g., "1st", "2nd", "1°", "1.").
/// </summary>
/// <remarks>
///     <para>
///         Ordinal formatting varies significantly by language:
///         <list type="bullet">
///             <item>English: 1st, 2nd, 3rd, 4th... (special rules for 11th, 12th, 13th)</item>
///             <item>Italian/Spanish/Portuguese: 1°, 2°, 3°...</item>
///             <item>German/Swedish/Norwegian: 1., 2., 3...</item>
///             <item>French: 1er (masculine), 1ère (feminine), 2e, 3e...</item>
///             <item>Russian: 1-й, 2-й, 3-й...</item>
///             <item>Chinese/Japanese: 第1, 第2, 第3...</item>
///         </list>
///     </para>
/// </remarks>
/// <example>
///     <code>
/// var formatter = new OrdinalFormatter("en");
/// formatter.Format(1);   // "1st"
/// formatter.Format(2);   // "2nd"
/// formatter.Format(11);  // "11th"
/// formatter.Format(21);  // "21st"
/// 
/// var itFormatter = new OrdinalFormatter("it");
/// itFormatter.Format(1); // "1°"
/// </code>
/// </example>
public sealed class OrdinalFormatter
{
    private readonly OrdinalGender _gender;
    private readonly string _language;

    /// <summary>
    ///     Creates a new OrdinalFormatter using the current culture from I18nContext.
    /// </summary>
    public OrdinalFormatter() : this(I18NContext.Current.CultureCode)
    {
    }

    /// <summary>
    ///     Creates a new OrdinalFormatter for a specific culture.
    /// </summary>
    /// <param name="culture">The culture code (e.g., "en", "it-IT").</param>
    /// <param name="gender">The grammatical gender (affects French ordinals).</param>
    public OrdinalFormatter(string culture, OrdinalGender gender = OrdinalGender.Masculine)
    {
        _language = LanguageHelper.GetLanguage(culture);
        _gender = gender;
    }

    /// <summary>
    ///     Gets a formatter for the current culture.
    /// </summary>
    public static OrdinalFormatter Current => new();

    /// <summary>
    ///     Formats a number as an ordinal string.
    /// </summary>
    /// <param name="number">The number to format.</param>
    /// <returns>The ordinal representation (e.g., "1st", "2nd").</returns>
    public string Format(int number)
    {
        return _language switch
        {
            "en" => FormatEnglish(number),
            "it" or "es" or "pt" => FormatRomance(number),
            "de" or "sv" or "no" or "da" or "nl" => FormatGermanic(number),
            "fr" => FormatFrench(number),
            "ru" or "uk" or "pl" or "cs" or "sk" => FormatSlavic(number),
            "zh" or "ja" => FormatEastAsian(number),
            "ko" => FormatKorean(number),
            "ar" => FormatArabic(number),
            "fi" => FormatFinnish(number),
            "hu" => FormatHungarian(number),
            _ => FormatEnglish(number) // Default to English
        };
    }

    /// <summary>
    ///     Formats a long number as an ordinal string.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside int range.</exception>
    public string Format(long number)
    {
        if (number < int.MinValue || number > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(number),
                $"Value {number} is outside the supported ordinal range [{int.MinValue}, {int.MaxValue}].");
        return Format((int)number);
    }

    // English: 1st, 2nd, 3rd, 4th... with exceptions for 11th, 12th, 13th
    private static string FormatEnglish(int number)
    {
        var abs = Math.Abs(number);
        var lastTwo = abs % 100;
        var lastOne = abs % 10;

        // Special case: 11, 12, 13 always use "th"
        if (lastTwo is >= 11 and <= 13)
            return $"{number}th";

        return lastOne switch
        {
            1 => $"{number}st",
            2 => $"{number}nd",
            3 => $"{number}rd",
            _ => $"{number}th"
        };
    }

    // Romance languages (Italian, Spanish, Portuguese): 1°, 2°, 3°...
    private static string FormatRomance(int number)
    {
        return $"{number}°";
    }

    // Germanic languages (German, Swedish, Norwegian, Danish, Dutch): 1., 2., 3...
    private static string FormatGermanic(int number)
    {
        return $"{number}.";
    }

    // French: 1er/1ère, 2e, 3e... (1st has gender distinction)
    private string FormatFrench(int number)
    {
        if (Math.Abs(number) == 1)
            return _gender == OrdinalGender.Feminine ? $"{number}ère" : $"{number}er";

        return $"{number}e";
    }

    // Slavic languages (Russian, Ukrainian, Polish, Czech, Slovak): 1-й, 2-й, 3-й...
    private static string FormatSlavic(int number)
    {
        return $"{number}-й";
    }

    // East Asian (Chinese, Japanese): 第1, 第2, 第3...
    private static string FormatEastAsian(int number)
    {
        return $"第{number}";
    }

    // Korean: 제1, 제2, 제3...
    private static string FormatKorean(int number)
    {
        return $"제{number}";
    }

    // Arabic: uses specific ordinal forms
    private static string FormatArabic(int number)
    {
        // Simplified - Arabic ordinals are complex
        return $"{number}.";
    }

    // Finnish: 1., 2., 3... (with trailing period)
    private static string FormatFinnish(int number)
    {
        return $"{number}.";
    }

    // Hungarian: 1., 2., 3... (with trailing period)
    private static string FormatHungarian(int number)
    {
        return $"{number}.";
    }

    /// <summary>
    ///     Creates a formatter for a specific culture.
    /// </summary>
    public static OrdinalFormatter ForCulture(string culture, OrdinalGender gender = OrdinalGender.Masculine)
    {
        return new OrdinalFormatter(culture, gender);
    }
}
