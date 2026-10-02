using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Resolves internationalization configuration by merging settings from all registered providers.
/// </summary>
/// <remarks>
///     <para>
///         Providers are processed in priority order (lowest to highest).
///         Higher-priority providers override settings from lower-priority ones.
///     </para>
///     <para>
///         If no provider sets a required value (e.g., DefaultUICulture),
///         an <see cref="I18NConfigurationException"/> is thrown.
///     </para>
/// </remarks>
public sealed class I18NConfigResolver
{
    private readonly IReadOnlyList<II18NConfigProvider> _providers;

    /// <summary>
    ///     Initializes a new instance of the <see cref="I18NConfigResolver"/> class.
    /// </summary>
    /// <param name="providers">The configuration providers.</param>
    public I18NConfigResolver(IEnumerable<II18NConfigProvider> providers)
    {
        // Sort by priority ascending (so higher priority providers are processed last and override)
        _providers = providers.OrderBy(p => p.Priority).ToList();
        _cachedMergedConfig = new Lazy<I18NConfig>(MergeAllProviders);
    }

    /// <summary>
    ///     Resolves the merged configuration from all providers.
    /// </summary>
    /// <returns>The merged configuration.</returns>
    /// <exception cref="I18NConfigurationException">
    ///     Thrown if required configuration values are missing.
    /// </exception>
    public I18NConfig Resolve()
    {
        var merged = GetMergedConfig();

        // Validate required fields
        ValidateConfiguration(merged);

        return merged;
    }

    /// <summary>
    ///     Checks if a culture is supported based on the current configuration.
    /// </summary>
    /// <param name="culture">The culture to check.</param>
    /// <returns>True if the culture is supported; otherwise, false.</returns>
    public bool IsCultureSupported(CultureCode culture)
    {
        var config = GetMergedConfig();

        // If no supported cultures configured, all cultures are supported
        if (config.SupportedCultures is null || config.SupportedCultures.Count == 0)
            return true;

        // Check exact match
        if (config.SupportedCultures.Contains(culture))
            return true;

        // Check language-only match (e.g., "it-IT" requested, "it" supported)
        return config.SupportedCultures.Any(c => c.Language.Equals(culture.Language));
    }

    /// <summary>
    ///     Finds the best matching supported culture for the requested culture.
    /// </summary>
    /// <param name="requestedCulture">The requested culture.</param>
    /// <returns>
    ///     The best matching supported culture, or null if no match found.
    /// </returns>
    public CultureCode? FindBestMatch(CultureCode requestedCulture)
    {
        var config = GetMergedConfig();

        if (config.SupportedCultures is null || config.SupportedCultures.Count == 0)
            return requestedCulture; // No restrictions, use as-is

        // 1. Exact match
        var exact = config.SupportedCultures.FirstOrDefault(c => c.Equals(requestedCulture));
        if (!string.IsNullOrEmpty(exact.Code))
            return exact;

        // 2. Language match (e.g., "it-IT" requested, "it" supported, or vice versa)
        var languageMatch = config.SupportedCultures
            .FirstOrDefault(c => c.Language.Equals(requestedCulture.Language));
        if (!string.IsNullOrEmpty(languageMatch.Code))
            return languageMatch;

        // 3. No match
        return null;
    }

    // Providers are set at construction and never change, so the merged result is stable.
    // Lazy<T> (default LazyThreadSafetyMode.ExecutionAndPublication) ensures the factory
    // runs exactly once even under concurrent first-calls.
    private readonly Lazy<I18NConfig> _cachedMergedConfig;

    private I18NConfig GetMergedConfig() => _cachedMergedConfig.Value;

    private I18NConfig MergeAllProviders()
    {
        var result = new I18NConfig();

        foreach (var provider in _providers)
        {
            var config = provider.GetConfiguration();
            if (config is null)
                continue;

            // Merge: non-null values override
            result = result with
            {
                DefaultUICulture = config.DefaultUICulture ?? result.DefaultUICulture,
                DefaultDataCulture = config.DefaultDataCulture ?? result.DefaultDataCulture,
                SyncScopes = config.SyncScopes ?? result.SyncScopes,
                SupportedCultures = config.SupportedCultures ?? result.SupportedCultures,
                PreferredCurrency = config.PreferredCurrency ?? result.PreferredCurrency,
                CustomScopes = MergeCustomScopes(result.CustomScopes, config.CustomScopes)
            };
        }

        return result;
    }

    private static IReadOnlyDictionary<string, CultureCode>? MergeCustomScopes(
        IReadOnlyDictionary<string, CultureCode>? existing,
        IReadOnlyDictionary<string, CultureCode>? incoming)
    {
        if (incoming is null)
            return existing;

        if (existing is null)
            return incoming;

        // Merge dictionaries, incoming overrides existing
        var merged = new Dictionary<string, CultureCode>(existing);
        foreach (var (key, value) in incoming)
        {
            merged[key] = value;
        }

        return merged;
    }

    private static void ValidateConfiguration(I18NConfig config)
    {
        // UI culture is required
        if (config.DefaultUICulture is null || string.IsNullOrEmpty(config.DefaultUICulture.Value.Code))
        {
            throw I18NConfigurationException.NoUICultureConfigured();
        }

        // Supported cultures must not be empty if specified
        if (config.SupportedCultures is not null && config.SupportedCultures.Count == 0)
        {
            throw I18NConfigurationException.NoSupportedCulturesConfigured();
        }
    }
}
