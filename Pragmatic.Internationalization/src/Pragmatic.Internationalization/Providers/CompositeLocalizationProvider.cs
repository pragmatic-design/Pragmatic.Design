using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Providers;

/// <summary>
///     Combines multiple localization providers with priority-based lookup.
///     Higher priority providers are checked first.
/// </summary>
public sealed class CompositeLocalizationProvider : ILocalizationProvider
{
    private readonly List<ILocalizationProvider> _providers;
    private readonly Lazy<IReadOnlyList<string>> _supportedCultures;

    /// <summary>
    ///     Creates a composite provider from multiple providers.
    ///     Providers are automatically sorted by priority (descending).
    /// </summary>
    public CompositeLocalizationProvider(IEnumerable<ILocalizationProvider> providers)
    {
        _providers = providers.OrderByDescending(p => p.Priority).ToList();
        _supportedCultures = new Lazy<IReadOnlyList<string>>(ComputeSupportedCultures);
    }

    /// <summary>
    ///     Gets the number of providers in this composite.
    /// </summary>
    public int ProviderCount => _providers.Count;

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         The culture first, in every provider, then its parent one subtag at a time
    ///         (<c>en-US</c>, then <c>en</c>): a module ships <c>en.json</c> and its host answers in
    ///         <c>en-US</c>. Without the walk the providers that look a culture up exactly (the one the
    ///         generator embeds in a module among them) answer nothing, and every caller that asks the
    ///         composite directly (the error resolver above all) gets the key instead of the sentence.
    ///     </para>
    ///     <para>
    ///         Every provider is asked for the exact culture before any is asked for the parent, so a
    ///         translation written for the region always wins over one written for the language,
    ///         whichever provider holds it.
    ///     </para>
    /// </remarks>
    public string? GetString(string key, string culture)
    {
        foreach (var candidate in CultureAndParents(culture))
        {
            foreach (var provider in _providers)
            {
                var value = provider.GetString(key, candidate);
                if (value is not null)
                    return value;
            }
        }

        return null;
    }

    /// <inheritdoc />
    /// <remarks>Walks the culture's parents as <see cref="GetString" /> does.</remarks>
    public PluralString? GetPlural(string key, string culture)
    {
        foreach (var candidate in CultureAndParents(culture))
        {
            foreach (var provider in _providers)
            {
                var value = provider.GetPlural(key, candidate);
                if (value is not null)
                    return value;
            }
        }

        return null;
    }

    /// <summary>The culture, then each parent down to the language: <c>zh-Hant-CN</c>, <c>zh-Hant</c>, <c>zh</c>.</summary>
    private static IEnumerable<string> CultureAndParents(string culture)
    {
        var current = culture;
        while (true)
        {
            yield return current;
            var lastSubtag = current.LastIndexOf('-');
            if (lastSubtag <= 0)
                yield break;
            current = current[..lastSubtag];
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAll(string culture)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        // Merge from lowest to highest priority so high-priority overrides low-priority
        foreach (var provider in _providers.AsEnumerable().Reverse())
            foreach (var kvp in provider.GetAll(culture))
                result[kvp.Key] = kvp.Value;

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PluralString> GetAllPlurals(string culture)
    {
        var result = new Dictionary<string, PluralString>(StringComparer.Ordinal);

        // Merge from lowest to highest priority
        foreach (var provider in _providers.AsEnumerable().Reverse())
            foreach (var kvp in provider.GetAllPlurals(culture))
                result[kvp.Key] = kvp.Value;

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedCultures => _supportedCultures.Value;

    /// <inheritdoc />
    public int Priority => _providers.Count > 0 ? _providers.Max(p => p.Priority) : 0;

    private IReadOnlyList<string> ComputeSupportedCultures()
    {
        return _providers
            .SelectMany(p => p.SupportedCultures)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();
    }
}