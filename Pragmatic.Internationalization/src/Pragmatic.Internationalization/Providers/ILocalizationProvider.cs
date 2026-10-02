using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Providers;

/// <summary>
///     Interface for localization providers that supply translated strings.
///     Multiple providers can be chained to support fallback and override scenarios.
/// </summary>
public interface ILocalizationProvider
{
    /// <summary>
    ///     Gets the list of supported cultures by this provider.
    /// </summary>
    IReadOnlyList<string> SupportedCultures { get; }

    /// <summary>
    ///     Gets the priority of this provider. Higher priority providers are checked first.
    ///     Default is 0. Database provider might use 100 to override JSON.
    /// </summary>
    int Priority => 0;

    /// <summary>
    ///     Gets a localized string for the specified key and culture.
    ///     Returns null if the translation is not found.
    /// </summary>
    /// <param name="key">The translation key (e.g., "Welcome", "Errors.NotFound").</param>
    /// <param name="culture">The culture code (e.g., "en", "it-IT").</param>
    /// <returns>The translated string, or null if not found.</returns>
    string? GetString(string key, string culture);

    /// <summary>
    ///     Gets a plural string for the specified key and culture.
    ///     Returns null if the plural forms are not found.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="culture">The culture code.</param>
    /// <returns>The plural string with all forms, or null if not found.</returns>
    PluralString? GetPlural(string key, string culture);

    /// <summary>
    ///     Gets all translations for the specified culture.
    ///     Used for exporting translations to frontend.
    /// </summary>
    /// <param name="culture">The culture code.</param>
    /// <returns>Dictionary of key-value pairs for simple strings.</returns>
    IReadOnlyDictionary<string, string> GetAll(string culture);

    /// <summary>
    ///     Gets all plural translations for the specified culture.
    /// </summary>
    /// <param name="culture">The culture code.</param>
    /// <returns>Dictionary of key to plural forms.</returns>
    IReadOnlyDictionary<string, PluralString> GetAllPlurals(string culture);
}