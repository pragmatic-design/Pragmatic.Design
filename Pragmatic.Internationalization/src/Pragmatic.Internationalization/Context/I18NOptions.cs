using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Options for configuring internationalization via dependency injection.
/// </summary>
/// <remarks>
///     <para>
///         These options are used by SystemConfigProvider (in AspNetCore package) to provide
///         default configuration values. Higher-priority providers can override these.
///     </para>
///     <para>
///         Configure via <c>AddPragmaticInternationalization(options => ...)</c>
///         or bind from appsettings.json via the "I18N" section.
///     </para>
/// </remarks>
/// <example>
/// <code>
/// // Configuration via code
/// services.AddPragmaticInternationalization(options =>
/// {
///     options.DefaultUICulture = CultureCode.Italian;
///     options.SupportedCultures = [CultureCode.Italian, CultureCode.English];
///     options.SyncScopes = true;
/// });
///
/// // Configuration via appsettings.json
/// {
///   "I18N": {
///     "DefaultUICulture": "it-IT",
///     "SupportedCultures": ["it-IT", "en-US"],
///     "SyncScopes": true
///   }
/// }
/// </code>
/// </example>
public sealed class I18NOptions
{
    /// <summary>
    ///     The configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "I18N";

    #region Culture Settings

    /// <summary>
    ///     Gets or sets the default UI culture.
    /// </summary>
    /// <remarks>
    ///     Used for display labels, messages, and user-facing content.
    /// </remarks>
    public CultureCode? DefaultUICulture { get; set; }

    /// <summary>
    ///     Gets or sets the default data culture.
    /// </summary>
    /// <remarks>
    ///     Used for data storage, APIs, and serialization.
    ///     If not set, defaults to <see cref="DefaultUICulture"/>.
    /// </remarks>
    public CultureCode? DefaultDataCulture { get; set; }

    /// <summary>
    ///     Gets or sets whether to synchronize UI and Data cultures.
    /// </summary>
    /// <remarks>
    ///     When true, setting the UI culture automatically sets the Data culture.
    ///     Useful for simple sites where regional differences don't matter.
    ///     Default: false.
    /// </remarks>
    public bool SyncScopes { get; set; }

    /// <summary>
    ///     Gets or sets the list of supported cultures.
    /// </summary>
    /// <remarks>
    ///     If a requested culture is not in this list, the system falls back to
    ///     the default culture or returns an error.
    /// </remarks>
    public IReadOnlyList<CultureCode>? SupportedCultures { get; set; }

    /// <summary>
    ///     Gets or sets custom scope default cultures.
    /// </summary>
    /// <remarks>
    ///     Maps scope names to their default cultures. Populated automatically
    ///     when registering typed scopes via <c>I18NBuilder.AddScope&lt;TScope&gt;()</c>.
    /// </remarks>
    public IDictionary<string, CultureCode>? CustomScopes { get; set; }

    /// <summary>
    ///     Gets or sets the query-string key used to override the request culture.
    /// </summary>
    /// <remarks>
    ///     The ASP.NET Core culture middleware reads this key from the request query string
    ///     (e.g. <c>?culture=it-IT</c>). Default: <c>"culture"</c>.
    /// </remarks>
    public string QueryStringKey { get; set; } = "culture";

    #endregion

    #region Localization Settings (for T class / string resources)

    /// <summary>
    ///     Path to the translations folder (relative to content root).
    /// </summary>
    public string TranslationsPath { get; set; } = "translations";

    /// <summary>
    ///     Whether to enable hot reload of translation files in development.
    /// </summary>
    public bool EnableHotReload { get; set; }

    /// <summary>
    ///     Custom fallback chain provider.
    /// </summary>
    /// <remarks>
    ///     Given a culture code, returns the ordered list of fallback cultures.
    ///     If not set, uses the default chain: requested → language-only → default.
    /// </remarks>
    public Func<CultureCode, IReadOnlyList<CultureCode>>? FallbackChain { get; set; }

    #endregion

    #region Diagnostics

    /// <summary>
    ///     Whether to enable diagnostics (ActivitySource for tracing).
    /// </summary>
    public bool EnableDiagnostics { get; set; }

    #endregion

    #region Helpers

    /// <summary>
    ///     Gets the fallback cultures for the specified culture.
    /// </summary>
    public IReadOnlyList<CultureCode> GetFallbacks(CultureCode culture)
    {
        if (FallbackChain is not null)
            return FallbackChain(culture);

        return GetDefaultFallbacks(culture);
    }

    private IReadOnlyList<CultureCode> GetDefaultFallbacks(CultureCode culture)
    {
        var fallbacks = new List<CultureCode>();

        // Add language-only culture if this has a country (e.g., "it-IT" → "it")
        if (culture.Country.HasValue)
        {
            var languageOnly = new CultureCode(culture.Language);
            if (!languageOnly.Equals(DefaultUICulture))
                fallbacks.Add(languageOnly);
        }

        // Add default culture if not already the requested culture
        if (DefaultUICulture.HasValue && !culture.Equals(DefaultUICulture.Value))
            fallbacks.Add(DefaultUICulture.Value);

        return fallbacks;
    }

    #endregion
}
