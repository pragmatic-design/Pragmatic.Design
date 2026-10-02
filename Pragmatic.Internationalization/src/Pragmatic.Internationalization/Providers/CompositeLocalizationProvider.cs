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
    public string? GetString(string key, string culture)
    {
        foreach (var provider in _providers)
        {
            var value = provider.GetString(key, culture);
            if (value is not null)
                return value;
        }

        return null;
    }

    /// <inheritdoc />
    public PluralString? GetPlural(string key, string culture)
    {
        foreach (var provider in _providers)
        {
            var value = provider.GetPlural(key, culture);
            if (value is not null)
                return value;
        }

        return null;
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