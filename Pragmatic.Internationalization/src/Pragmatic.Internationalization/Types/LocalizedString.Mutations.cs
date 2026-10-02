using Pragmatic.Internationalization.Context;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Mutation, factory and query members of <see cref="LocalizedString"/>: setting/removing
///     translations, the <c>From</c> factories, and culture/text lookups.
/// </summary>
public sealed partial class LocalizedString
{
    // ══════════════════════════════════════════════════════════════
    // MODIFICATION
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Sets translation for a culture. Returns this for fluent chaining.
    /// </summary>
    /// <exception cref="InvalidOperationException">This instance is frozen (<see cref="Empty" />).</exception>
    public LocalizedString Set(string culture, string value)
    {
        ThrowIfFrozen();
        var copy = new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase)
        {
            [culture] = value
        };
        _values = copy;
        return this;
    }

    /// <summary>
    ///     Sets translation for current culture. Returns this for fluent chaining.
    /// </summary>
    /// <exception cref="InvalidOperationException">This instance is frozen (<see cref="Empty" />).</exception>
    public LocalizedString SetCurrent(string value)
    {
        return Set(I18NContext.Current.CultureCode, value);
    }

    /// <summary>
    ///     Removes translation for a culture.
    /// </summary>
    /// <exception cref="InvalidOperationException">This instance is frozen (<see cref="Empty" />).</exception>
    public bool Remove(string culture)
    {
        ThrowIfFrozen();
        if (!_values.ContainsKey(culture))
            return false;

        var copy = new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
        copy.Remove(culture);
        _values = copy;
        return true;
    }

    /// <summary>
    ///     Clears all translations.
    /// </summary>
    /// <exception cref="InvalidOperationException">This instance is frozen (<see cref="Empty" />).</exception>
    public void Clear()
    {
        ThrowIfFrozen();
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException(
                "LocalizedString.Empty is a shared frozen instance and cannot be mutated. " +
                "Create a new instance with 'new LocalizedString()' or 'LocalizedString.From(...)'.");
    }

    // ══════════════════════════════════════════════════════════════
    // FACTORIES
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Creates LocalizedString with default culture value.
    /// </summary>
    public static LocalizedString From(string defaultValue)
    {
        var t = new LocalizedString();
        t._values[DefaultFallbackCulture] = defaultValue;
        return t;
    }

    /// <summary>
    ///     Creates LocalizedString from culture-value pairs.
    /// </summary>
    public static LocalizedString From(params (string culture, string value)[] values)
    {
        var t = new LocalizedString();
        foreach (var (culture, value) in values)
            t._values[culture] = value;
        return t;
    }

    /// <summary>
    ///     Creates LocalizedString from a dictionary.
    /// </summary>
    public static LocalizedString From(IDictionary<string, string> values)
    {
        var t = new LocalizedString();
        foreach (var kvp in values)
            t._values[kvp.Key] = kvp.Value;
        return t;
    }

    // ══════════════════════════════════════════════════════════════
    // QUERY HELPERS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Checks if the translation for the specified culture contains the text.
    ///     Uses current culture if not specified.
    /// </summary>
    public bool Contains(string text, string? culture = null)
    {
        culture ??= I18NContext.Current.CultureCode;
        var value = Get(culture);
        return value.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Checks if translation exists for culture (exact, no fallback).
    /// </summary>
    public bool HasCulture(string culture)
    {
        return _values.ContainsKey(culture);
    }
}
