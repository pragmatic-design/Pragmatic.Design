using System.Collections.Frozen;
using System.Globalization;
using Pragmatic.Internationalization.Context;

namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Formats large numbers in a human-friendly way (e.g., "1.5K", "2.3M", "1.2B").
/// </summary>
/// <remarks>
///     <para>
///         The formatter uses culture-specific number formatting (decimal separator)
///         and localized suffixes for thousands (K), millions (M), billions (B), and trillions (T).
///     </para>
///     <para>
///         Common abbreviations by culture:
///         <list type="bullet">
///             <item>English: K, M, B, T</item>
///             <item>Italian: K, M, Mld, Bln</item>
///             <item>German: Tsd, Mio, Mrd, Bio</item>
///             <item>French: k, M, Md, Bn</item>
///         </list>
///     </para>
/// </remarks>
/// <example>
///     <code>
/// var formatter = new QuantityFormatter();
/// formatter.Format(1500);     // "1.5K" (en) or "1,5K" (it)
/// formatter.Format(2300000);  // "2.3M"
/// formatter.Format(1200000000); // "1.2B"
/// </code>
/// </example>
public sealed class QuantityFormatter
{
    private static readonly FrozenDictionary<string, QuantitySuffixes> SSuffixesByLanguage =
        new Dictionary<string, QuantitySuffixes>(StringComparer.OrdinalIgnoreCase)
        {
            // English (default)
            ["en"] = new QuantitySuffixes("K", "M", "B", "T"),

            // Italian
            ["it"] = new QuantitySuffixes("K", "M", "Mld", "Bln"),

            // German
            ["de"] = new QuantitySuffixes("Tsd", "Mio", "Mrd", "Bio"),

            // French
            ["fr"] = new QuantitySuffixes("k", "M", "Md", "Bn"),

            // Spanish
            ["es"] = new QuantitySuffixes("K", "M", "MM", "B"),

            // Portuguese
            ["pt"] = new QuantitySuffixes("K", "M", "B", "T"),

            // Russian
            ["ru"] = new QuantitySuffixes("тыс", "млн", "млрд", "трлн"),

            // Chinese
            ["zh"] = new QuantitySuffixes("千", "百万", "十亿", "万亿"),

            // Japanese
            ["ja"] = new QuantitySuffixes("千", "百万", "十億", "兆"),

            // Korean
            ["ko"] = new QuantitySuffixes("천", "백만", "십억", "조"),

            // Arabic
            ["ar"] = new QuantitySuffixes("ألف", "مليون", "مليار", "تريليون"),

            // Dutch
            ["nl"] = new QuantitySuffixes("K", "M", "Mrd", "Bln"),

            // Polish
            ["pl"] = new QuantitySuffixes("tys", "mln", "mld", "bln"),

            // Swedish
            ["sv"] = new QuantitySuffixes("K", "M", "Md", "Bn"),

            // Norwegian
            ["no"] = new QuantitySuffixes("K", "M", "Mrd", "Bn"),

            // Danish
            ["da"] = new QuantitySuffixes("K", "M", "Mia", "Bio"),

            // Finnish
            ["fi"] = new QuantitySuffixes("t", "M", "Mrd", "Bn")
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly string _culture;
    private readonly int _decimals;
    private readonly QuantitySuffixes _suffixes;

    /// <summary>
    ///     Creates a new QuantityFormatter using the current culture from I18nContext.
    /// </summary>
    public QuantityFormatter() : this(I18NContext.Current.CultureCode)
    {
    }

    /// <summary>
    ///     Creates a new QuantityFormatter for a specific culture.
    /// </summary>
    /// <param name="culture">The culture code (e.g., "en", "it-IT").</param>
    /// <param name="decimals">Number of decimal places to show (default: 1).</param>
    public QuantityFormatter(string culture, int decimals = 1)
    {
        _culture = culture;
        _decimals = decimals;
        _suffixes = GetSuffixesForCulture(culture);
    }

    /// <summary>
    ///     Creates a new QuantityFormatter with custom suffixes.
    /// </summary>
    /// <param name="culture">The culture code for number formatting.</param>
    /// <param name="suffixes">Custom suffixes for K, M, B, T.</param>
    /// <param name="decimals">Number of decimal places to show (default: 1).</param>
    public QuantityFormatter(string culture, QuantitySuffixes suffixes, int decimals = 1)
    {
        _culture = culture;
        _suffixes = suffixes;
        _decimals = decimals;
    }

    /// <summary>
    ///     Gets a formatter for the current culture.
    /// </summary>
    public static QuantityFormatter Current => new();

    /// <summary>
    ///     Formats a number as a human-readable quantity.
    /// </summary>
    /// <param name="value">The number to format.</param>
    /// <returns>A formatted string like "1.5K" or "2.3M".</returns>
    public string Format(long value)
    {
        var absValue = Math.Abs(value);
        var sign = value < 0 ? "-" : "";
        var culture = CultureInfo.GetCultureInfo(_culture);

        return absValue switch
        {
            >= 1_000_000_000_000 =>
                $"{sign}{FormatWithDecimals(absValue / 1_000_000_000_000.0, culture)}{_suffixes.Trillion}",
            >= 1_000_000_000 => $"{sign}{FormatWithDecimals(absValue / 1_000_000_000.0, culture)}{_suffixes.Billion}",
            >= 1_000_000 => $"{sign}{FormatWithDecimals(absValue / 1_000_000.0, culture)}{_suffixes.Million}",
            >= 1_000 => $"{sign}{FormatWithDecimals(absValue / 1_000.0, culture)}{_suffixes.Thousand}",
            _ => value.ToString("N0", culture)
        };
    }

    /// <summary>
    ///     Formats a decimal number as a human-readable quantity.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside long range.</exception>
    public string Format(decimal value)
    {
        var rounded = Math.Round(value);
        if (rounded < long.MinValue || rounded > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Value {value} is outside the supported quantity range.");
        return Format((long)rounded);
    }

    /// <summary>
    ///     Formats a double number as a human-readable quantity.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside long range or is not finite.</exception>
    public string Format(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be a finite number.");
        var rounded = Math.Round(value);
        if (rounded > long.MaxValue || rounded < long.MinValue)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Value {value} is outside the supported quantity range.");
        return Format((long)rounded);
    }

    private string FormatWithDecimals(double value, CultureInfo culture)
    {
        // Remove trailing zeros after decimal
        var formatted = value.ToString($"F{_decimals}", culture);
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;

        if (formatted.Contains(decimalSeparator))
            formatted = formatted.TrimEnd('0').TrimEnd(decimalSeparator.ToCharArray());

        return formatted;
    }

    private static QuantitySuffixes GetSuffixesForCulture(string culture)
    {
        // Try exact match
        if (SSuffixesByLanguage.TryGetValue(culture, out var suffixes))
            return suffixes;

        // Try language part (e.g., "en" from "en-US")
        var language = LanguageHelper.GetLanguage(culture);
        if (SSuffixesByLanguage.TryGetValue(language, out suffixes))
            return suffixes;

        // Default to English
        return SSuffixesByLanguage["en"];
    }

    /// <summary>
    ///     Creates a formatter for a specific culture.
    /// </summary>
    public static QuantityFormatter ForCulture(string culture)
    {
        return new QuantityFormatter(culture);
    }
}
