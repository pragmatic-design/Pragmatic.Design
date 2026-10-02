using System.Text;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Formats TimeSpan durations in a human-readable way (e.g., "2h 30m", "1d 5h").
/// </summary>
/// <remarks>
///     <para>
///         The formatter supports multiple formats:
///         <list type="bullet">
///             <item><see cref="DurationFormat.Short" />: "2h 30m", "1d 5h"</item>
///             <item><see cref="DurationFormat.Long" />: "2 hours 30 minutes"</item>
///             <item><see cref="DurationFormat.Compact" />: "2:30:00"</item>
///         </list>
///     </para>
///     <para>
///         Language-specific suffixes are used for short and long formats.
///         Slavic languages use plural rules (via <see cref="PluralRules"/>) for correct "few" forms.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// var formatter = new DurationFormatter("en");
/// formatter.Format(TimeSpan.FromMinutes(150));  // "2h 30m"
/// formatter.Format(TimeSpan.FromHours(26));     // "1d 2h"
///
/// var longFormatter = new DurationFormatter("en", DurationFormat.Long);
/// longFormatter.Format(TimeSpan.FromMinutes(90)); // "1 hour 30 minutes"
/// </code>
/// </example>
public sealed partial class DurationFormatter
{
    private readonly DurationFormat _format;

    private readonly string _language;
    private readonly int _maxParts;
    private readonly bool _showZeroParts;

    /// <summary>
    ///     Creates a new DurationFormatter using the current culture from I18nContext.
    /// </summary>
    public DurationFormatter() : this(I18NContext.Current.CultureCode)
    {
    }

    /// <summary>
    ///     Creates a new DurationFormatter for a specific culture.
    /// </summary>
    /// <param name="culture">The culture code (e.g., "en", "it-IT").</param>
    /// <param name="format">The output format (Short, Long, or Compact).</param>
    /// <param name="maxParts">Maximum number of time parts to include (e.g., 2 = "2h 30m", not "2h 30m 15s").</param>
    /// <param name="showZeroParts">Whether to show zero values (e.g., "2h 0m 30s" vs "2h 30s").</param>
    public DurationFormatter(
        string culture,
        DurationFormat format = DurationFormat.Short,
        int maxParts = 2,
        bool showZeroParts = false)
    {
        _language = LanguageHelper.GetLanguage(culture);
        _format = format;
        _maxParts = maxParts;
        _showZeroParts = showZeroParts;
    }

    /// <summary>
    ///     Gets a formatter for the current culture.
    /// </summary>
    public static DurationFormatter Current => new();

    /// <summary>
    ///     Formats a TimeSpan as a human-readable duration.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>A formatted duration string.</returns>
    public string Format(TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
            return FormatZero();

        var isNegative = duration < TimeSpan.Zero;
        var abs = duration.Duration();

        return _format switch
        {
            DurationFormat.Short => FormatShort(abs, isNegative),
            DurationFormat.Long => FormatLong(abs, isNegative),
            DurationFormat.Compact => FormatCompact(abs, isNegative),
            _ => FormatShort(abs, isNegative)
        };
    }

    /// <summary>
    ///     Formats a number of seconds as a human-readable duration.
    /// </summary>
    public string Format(double seconds)
    {
        return Format(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    ///     Formats a number of milliseconds as a human-readable duration.
    /// </summary>
    public string FormatMilliseconds(long milliseconds)
    {
        return Format(TimeSpan.FromMilliseconds(milliseconds));
    }

    private string FormatZero()
    {
        return _format switch
        {
            DurationFormat.Short => GetShortSuffixes().Seconds switch
            {
                var s => $"0{s}"
            },
            DurationFormat.Long => GetLongSuffixes() switch
            {
                var w => $"0 {w.Seconds.Plural}"
            },
            DurationFormat.Compact => "0:00",
            _ => "0s"
        };
    }

    private string FormatShort(TimeSpan duration, bool isNegative)
    {
        var suffixes = GetShortSuffixes();
        var parts = new List<string>();

        var days = (int)duration.TotalDays;
        var hours = duration.Hours;
        var minutes = duration.Minutes;
        var seconds = duration.Seconds;
        var milliseconds = duration.Milliseconds;

        if (days > 0 || _showZeroParts)
            parts.Add($"{days}{suffixes.Days}");

        if ((hours > 0 || _showZeroParts) && parts.Count < _maxParts)
            parts.Add($"{hours}{suffixes.Hours}");

        if ((minutes > 0 || _showZeroParts) && parts.Count < _maxParts)
            parts.Add($"{minutes}{suffixes.Minutes}");

        if ((seconds > 0 || _showZeroParts) && parts.Count < _maxParts)
            parts.Add($"{seconds}{suffixes.Seconds}");

        if ((milliseconds > 0 || _showZeroParts) && parts.Count < _maxParts)
            parts.Add($"{milliseconds}{suffixes.Milliseconds}");

        // Remove leading zeros if not showing zero parts
        if (!_showZeroParts)
            parts = parts.Where(p => !p.StartsWith("0")).Take(_maxParts).ToList();

        if (parts.Count == 0)
            parts.Add($"0{suffixes.Seconds}");

        var result = string.Join(" ", parts.Take(_maxParts));
        return isNegative ? $"-{result}" : result;
    }

    private string FormatLong(TimeSpan duration, bool isNegative)
    {
        var words = GetLongSuffixes();
        var usePluralRules = SlavicLanguages.Contains(_language);
        var parts = new List<string>();

        var days = (int)duration.TotalDays;
        var hours = duration.Hours;
        var minutes = duration.Minutes;
        var seconds = duration.Seconds;

        if (days > 0)
            parts.Add($"{days} {GetWordForm(days, words.Days, usePluralRules)}");

        if (hours > 0 && parts.Count < _maxParts)
            parts.Add($"{hours} {GetWordForm(hours, words.Hours, usePluralRules)}");

        if (minutes > 0 && parts.Count < _maxParts)
            parts.Add($"{minutes} {GetWordForm(minutes, words.Minutes, usePluralRules)}");

        if (seconds > 0 && parts.Count < _maxParts)
            parts.Add($"{seconds} {GetWordForm(seconds, words.Seconds, usePluralRules)}");

        if (parts.Count == 0)
            parts.Add($"0 {words.Seconds.Plural}");

        var result = string.Join(" ", parts.Take(_maxParts));
        return isNegative ? $"-{result}" : result;
    }

    /// <summary>
    ///     Selects the correct word form based on count and language plural rules.
    /// </summary>
    private string GetWordForm(int count, (string Singular, string Plural, string Few) word, bool usePluralRules)
    {
        if (!usePluralRules)
            return count == 1 ? word.Singular : word.Plural;

        var category = PluralRules.GetCategory(_language, count);
        return category switch
        {
            PluralCategory.One => word.Singular,
            PluralCategory.Few => word.Few,
            _ => word.Plural // Many and Other use the "many/other" form
        };
    }

    private static string FormatCompact(TimeSpan duration, bool isNegative)
    {
        var sb = new StringBuilder();

        if (isNegative)
            sb.Append('-');

        var days = (int)duration.TotalDays;
        if (days > 0)
        {
            sb.Append(days);
            sb.Append(':');
        }

        if (days > 0 || duration.Hours > 0)
        {
            sb.Append(duration.Hours.ToString(days > 0 ? "00" : "0"));
            sb.Append(':');
        }

        sb.Append(duration.Minutes.ToString(duration.Hours > 0 || days > 0 ? "00" : "0"));
        sb.Append(':');
        sb.Append(duration.Seconds.ToString("00"));

        return sb.ToString();
    }

    private DurationSuffixes GetShortSuffixes()
    {
        if (SShortSuffixes.TryGetValue(_language, out var suffixes))
            return suffixes;

        return SShortSuffixes["en"];
    }

    private DurationWords GetLongSuffixes()
    {
        if (SLongSuffixes.TryGetValue(_language, out var words))
            return words;

        return SLongSuffixes["en"];
    }

    /// <summary>
    ///     Creates a formatter for a specific culture.
    /// </summary>
    public static DurationFormatter ForCulture(string culture, DurationFormat format = DurationFormat.Short)
    {
        return new DurationFormatter(culture, format);
    }

    /// <summary>
    ///     Creates a short format formatter (e.g., "2h 30m").
    /// </summary>
    public static DurationFormatter Short(string? culture = null)
    {
        return new DurationFormatter(culture ?? I18NContext.Current.CultureCode);
    }

    /// <summary>
    ///     Creates a long format formatter (e.g., "2 hours 30 minutes").
    /// </summary>
    public static DurationFormatter Long(string? culture = null)
    {
        return new DurationFormatter(culture ?? I18NContext.Current.CultureCode, DurationFormat.Long);
    }

    /// <summary>
    ///     Creates a compact format formatter (e.g., "2:30:00").
    /// </summary>
    public static DurationFormatter Compact(string? culture = null)
    {
        return new DurationFormatter(culture ?? I18NContext.Current.CultureCode, DurationFormat.Compact);
    }
}
