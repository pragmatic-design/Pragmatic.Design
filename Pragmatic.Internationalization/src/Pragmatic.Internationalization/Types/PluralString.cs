using Pragmatic.Internationalization.Context;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Holds plural forms for a translation key.
///     Maps plural categories (zero, one, two, few, many, other) to their translated strings.
/// </summary>
public sealed class PluralString
{
    private readonly Dictionary<PluralCategory, string> _forms;

    /// <summary>
    ///     Creates a new PluralString with the specified forms.
    /// </summary>
    public PluralString(Dictionary<PluralCategory, string> forms)
    {
        ThrowIfNull(forms);
        _forms = forms;
    }

    /// <summary>
    ///     Gets the string for a specific plural category.
    ///     Falls back first to 'other', then to the first available form.
    ///     Returns an empty string only when no forms are defined at all.
    /// </summary>
    public string this[PluralCategory category] =>
        _forms.TryGetValue(category, out var value)
            ? value
            : _forms.TryGetValue(PluralCategory.Other, out var other)
                ? other
                : _forms.Count > 0
                    ? _forms.Values.First()
                    : "";

    /// <summary>
    ///     Gets all available plural categories.
    /// </summary>
    public IEnumerable<PluralCategory> Categories => _forms.Keys;

    /// <summary>
    ///     Gets the underlying forms dictionary.
    /// </summary>
    public IReadOnlyDictionary<PluralCategory, string> Forms => _forms;

    /// <summary>
    ///     Creates a PluralString from string-keyed dictionary (for JSON deserialization).
    /// </summary>
    public static PluralString FromDictionary(IReadOnlyDictionary<string, string> forms)
    {
        var parsed = new Dictionary<PluralCategory, string>();

        foreach (var (key, value) in forms)
            if (Enum.TryParse<PluralCategory>(key, true, out var category))
                parsed[category] = value;

        return new PluralString(parsed);
    }

    /// <summary>
    ///     Gets the appropriate form for the given count using the current culture's plural rules.
    /// </summary>
    public TranslationResult Format(int count, string key)
    {
        var culture = I18NContext.Current.CultureCode;
        var category = PluralRules.GetCategory(culture, count);
        var template = this[category];

        // Replace {count} placeholder
        var value = template.Replace("{count}", count.ToString());
        return TranslationResult.Found(key, value);
    }

    /// <summary>
    ///     Gets the appropriate form for the given count using explicit culture.
    /// </summary>
    public TranslationResult Format(int count, string key, string culture)
    {
        var category = PluralRules.GetCategory(culture, count);
        var template = this[category];

        var value = template.Replace("{count}", count.ToString());
        return TranslationResult.Found(key, value);
    }

    /// <summary>
    ///     Checks if a specific category is available.
    /// </summary>
    public bool HasCategory(PluralCategory category)
    {
        return _forms.ContainsKey(category);
    }
}