using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Providers;

/// <summary>
///     In-memory localization provider for testing and programmatic configuration.
/// </summary>
public sealed class InMemoryLocalizationProvider : ILocalizationProvider
{
    private readonly ConcurrentDictionary<string, CultureData> _cultures = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string? GetString(string key, string culture)
    {
        if (_cultures.TryGetValue(culture, out var data))
            return data.Strings.TryGetValue(key, out var value) ? value : null;
        return null;
    }

    /// <inheritdoc />
    public PluralString? GetPlural(string key, string culture)
    {
        if (_cultures.TryGetValue(culture, out var data))
            return data.Plurals.TryGetValue(key, out var value) ? value : null;
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAll(string culture)
    {
        return _cultures.TryGetValue(culture, out var data)
            ? new ReadOnlyDictionary<string, string>(data.Strings)
            : new Dictionary<string, string>();
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PluralString> GetAllPlurals(string culture)
    {
        return _cultures.TryGetValue(culture, out var data)
            ? new ReadOnlyDictionary<string, PluralString>(data.Plurals)
            : new Dictionary<string, PluralString>();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedCultures => [.. _cultures.Keys];

    /// <inheritdoc />
    public int Priority { get; set; }

    /// <summary>
    ///     Adds or updates a translation.
    /// </summary>
    public InMemoryLocalizationProvider AddString(string culture, string key, string value)
    {
        var data = GetOrCreateCultureData(culture);
        data.Strings[key] = value;
        return this;
    }

    /// <summary>
    ///     Adds or updates a plural translation.
    /// </summary>
    public InMemoryLocalizationProvider AddPlural(string culture, string key, PluralString plural)
    {
        var data = GetOrCreateCultureData(culture);
        data.Plurals[key] = plural;
        return this;
    }

    /// <summary>
    ///     Adds or updates a plural translation from category-value pairs.
    /// </summary>
    public InMemoryLocalizationProvider AddPlural(
        string culture,
        string key,
        params (PluralCategory category, string value)[] forms)
    {
        var dict = forms.ToDictionary(f => f.category, f => f.value);
        return AddPlural(culture, key, new PluralString(dict));
    }

    /// <summary>
    ///     Adds multiple translations at once.
    /// </summary>
    public InMemoryLocalizationProvider AddStrings(string culture,
        IEnumerable<KeyValuePair<string, string>> translations)
    {
        var data = GetOrCreateCultureData(culture);
        foreach (var kvp in translations)
            data.Strings[kvp.Key] = kvp.Value;
        return this;
    }

    /// <summary>
    ///     Removes a translation.
    /// </summary>
    public bool RemoveString(string culture, string key)
    {
        return _cultures.TryGetValue(culture, out var data) && data.Strings.Remove(key);
    }

    /// <summary>
    ///     Clears all translations for a culture.
    /// </summary>
    public void ClearCulture(string culture)
    {
        _cultures.TryRemove(culture, out _);
    }

    /// <summary>
    ///     Clears all translations.
    /// </summary>
    public void Clear()
    {
        _cultures.Clear();
    }

    private CultureData GetOrCreateCultureData(string culture)
    {
        return _cultures.GetOrAdd(culture, static _ => CultureDataFactory.CreateConcurrent());
    }
}