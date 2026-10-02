using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Pragmatic.Internationalization.Context;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Dictionary of culture → translated value for entity properties.
///     Stored as JSON column in database.
///     Provides automatic fallback chain for missing translations.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="LocalizedString" /> for entity properties that need to store
///         multiple translations inline (e.g., Product.Name, Category.Description).
///     </para>
///     <para>
///         For translation keys that reference external translation files,
///         use <see cref="LocalizationKey" /> instead.
///     </para>
/// </remarks>
/// <remarks>
///     <b>Warning:</b> This type is mutable. Do NOT use as a dictionary key or in hash-based collections.
///     Mutations via <see cref="Set"/>, <see cref="Remove"/>, <see cref="Clear"/> will invalidate hash codes.
/// </remarks>
/// <example>
///     <code>
/// // Create with default culture
/// var name = LocalizedString.From("Widget");
///
/// // Create with multiple cultures
/// var name = LocalizedString.From(
///     ("en", "Widget"),
///     ("it", "Componente"),
///     ("de", "Gerät")
/// );
///
/// // Access uses current culture automatically
/// Console.WriteLine(name.Value); // Uses I18nContext.Current
///
/// // Implicit conversion to string
/// string s = name; // Same as name.Value
/// </code>
/// </example>
public sealed partial class LocalizedString : IReadOnlyDictionary<string, string>, IEquatable<LocalizedString>
{
    // Mutations swap the whole dictionary (copy-on-write) so concurrent readers
    // always observe a consistent snapshot; the reference read/write is atomic.
    private Dictionary<string, string> _values;
    private readonly bool _frozen;

    /// <summary>
    ///     Creates an empty LocalizedString instance.
    /// </summary>
    public LocalizedString()
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private LocalizedString(bool frozen) : this()
    {
        _frozen = frozen;
    }

    /// <summary>
    ///     Creates a LocalizedString instance from existing values.
    /// </summary>
    private LocalizedString(Dictionary<string, string> values)
    {
        _values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════
    // ACCESS - Current Culture
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Gets translation for current culture with fallback chain.
    /// </summary>
    public string Value => Get(I18NContext.Current.CultureCode);

    /// <summary>
    ///     Gets translation for UI culture (user interface display).
    /// </summary>
    /// <remarks>
    ///     Equivalent to <see cref="Get(string)" /> with <see cref="I18NContext.Current" />.UICulture.
    /// </remarks>
    public string UIValue => Get(I18NContext.Current.UICulture.Code);

    /// <summary>
    ///     Gets translation for Data culture (storage/serialization).
    /// </summary>
    /// <remarks>
    ///     Use this when formatting data for APIs, exports, or database storage
    ///     where a consistent culture is needed regardless of user preferences.
    /// </remarks>
    public string DataValue => Get(I18NContext.Current.DataCulture.Code);

    /// <summary>
    ///     The canonical empty LocalizedString. This instance is frozen: any mutation
    ///     (<see cref="Set" />, <see cref="Remove" />, <see cref="Clear" />) throws.
    ///     Create a fresh instance (<c>new LocalizedString()</c> or <see cref="From(string)" />)
    ///     when you need a mutable empty value.
    /// </summary>
    public static readonly LocalizedString Empty = new(frozen: true);

    /// <summary>
    ///     Checks if translation exists for default culture ("en").
    /// </summary>
    public bool HasDefaultCulture =>
        _values.ContainsKey(DefaultFallbackCulture);

    /// <summary>
    ///     Gets all available cultures.
    /// </summary>
    public IEnumerable<string> Cultures => _values.Keys;

    /// <summary>
    ///     Checks if there are any translations.
    /// </summary>
    public bool IsEmpty => _values.Count == 0;

    // ══════════════════════════════════════════════════════════════
    // EQUALITY
    // ══════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public bool Equals(LocalizedString? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (_values.Count != other._values.Count)
            return false;

        foreach (var kvp in _values)
            if (!other._values.TryGetValue(kvp.Key, out var otherValue) ||
                kvp.Value != otherValue)
                return false;

        return true;
    }

    // ══════════════════════════════════════════════════════════════
    // ACCESS - Explicit Culture
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Gets translation for specific culture with fallback.
    /// </summary>
    public string this[string culture] => Get(culture);

    /// <summary>
    ///     Gets the number of translations.
    /// </summary>
    public int Count => _values.Count;

    // ══════════════════════════════════════════════════════════════
    // IReadOnlyDictionary Implementation
    // ══════════════════════════════════════════════════════════════

    IEnumerable<string> IReadOnlyDictionary<string, string>.Keys => _values.Keys;
    IEnumerable<string> IReadOnlyDictionary<string, string>.Values => _values.Values;

    bool IReadOnlyDictionary<string, string>.ContainsKey(string key)
    {
        return _values.ContainsKey(key);
    }

    bool IReadOnlyDictionary<string, string>.TryGetValue(string key, [MaybeNullWhen(false)] out string value)
    {
        return _values.TryGetValue(key, out value);
    }

    IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator()
    {
        return _values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _values.GetEnumerator();
    }

    // Ultimate fallback culture for translations when no match is found
    private const string DefaultFallbackCulture = "en";

    /// <summary>
    ///     Gets translation with fallback chain.
    /// </summary>
    public string Get(string culture)
    {
        // Direct match
        if (_values.TryGetValue(culture, out var value))
            return value;

        // Parent culture fallback (e.g., "it-IT" → "it")
        var dashIndex = culture.IndexOf('-');
        if (dashIndex > 0)
        {
            var parent = culture[..dashIndex];
            if (_values.TryGetValue(parent, out value))
                return value;
        }

        // Ultimate fallback: default culture
        if (_values.TryGetValue(DefaultFallbackCulture, out value))
            return value;

        // No translation found — return a deterministic value (ordinal-smallest key)
        // or empty. Dictionary enumeration order is unspecified, so pick by key.
        if (_values.Count == 0)
            return "";

        var fallbackKey = _values.Keys.OrderBy(static k => k, StringComparer.Ordinal).First();
        return _values[fallbackKey];
    }

    /// <summary>
    ///     Gets translation without fallback. Returns null if not found.
    /// </summary>
    public string? GetExact(string culture)
    {
        return _values.TryGetValue(culture, out var value) ? value : null;
    }

    /// <summary>
    ///     Tries to get the translation for a specific culture without fallback.
    /// </summary>
    public bool TryGetExact(string culture, [MaybeNullWhen(false)] out string value)
    {
        return _values.TryGetValue(culture, out value);
    }

    /// <summary>
    ///     Gets translation for a custom scope's culture.
    /// </summary>
    /// <param name="scopeName">The name of the custom scope.</param>
    /// <returns>The translation for the scope's culture, with fallback chain.</returns>
    /// <example>
    ///     <code>
    /// I18NContext.Current.SetScope("invoicing", CultureCode.German);
    /// var invoiceTitle = product.Name.GetForScope("invoicing"); // German translation
    /// </code>
    /// </example>
    public string GetForScope(string scopeName)
    {
        var scopeCulture = I18NContext.Current.GetScope(scopeName);
        return Get(scopeCulture.Code);
    }

    /// <summary>
    ///     Gets translation for a strongly-typed scope's culture.
    /// </summary>
    /// <typeparam name="TScope">The scope type implementing <see cref="Scopes.ICultureScope"/>.</typeparam>
    /// <returns>The translation for the scope's culture, with fallback chain.</returns>
    public string GetForScope<TScope>() where TScope : Scopes.ICultureScope
    {
        var scopeCulture = I18NContext.Current.GetScope<TScope>();
        return Get(scopeCulture.Code);
    }

    /// <summary>
    ///     Gets translation for a CultureCode with fallback chain.
    /// </summary>
    /// <param name="culture">The culture code.</param>
    /// <returns>The translation for the specified culture.</returns>
    public string Get(CultureCode culture)
    {
        return Get(culture.Code);
    }

    // ══════════════════════════════════════════════════════════════
    // CONVERSIONS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Implicit conversion to string (uses current culture).
    /// </summary>
    public static implicit operator string(LocalizedString t)
    {
        return t.Value;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }
}