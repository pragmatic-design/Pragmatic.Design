using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Providers;

/// <summary>
///     Interface for string localization with interpolation and pluralization support.
///     This is the main interface used by generated T class.
/// </summary>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]
public interface IStringLocalizer
{
    /// <summary>
    ///     Gets the current culture.
    /// </summary>
    string Culture { get; }

    /// <summary>
    ///     Gets a localized string for the specified key.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <returns>The translation result, or the key itself if not found.</returns>
    TranslationResult this[string key] { get; }

    /// <summary>
    ///     Gets a localized string with interpolated values.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="args">The values to interpolate.</param>
    /// <returns>The localized and formatted string.</returns>
    TranslationResult this[string key, params object[] args] { get; }

    /// <summary>
    ///     Gets a pluralized string for the specified key and count.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="count">The count to determine plural form.</param>
    /// <returns>The appropriate plural form with count interpolated.</returns>
    TranslationResult Plural(string key, int count);

    /// <summary>
    ///     Gets a pluralized string with additional interpolated values.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="count">The count to determine plural form.</param>
    /// <param name="args">Additional values to interpolate.</param>
    /// <returns>The formatted plural string.</returns>
    TranslationResult Plural(string key, int count, params object[] args);

    /// <summary>
    ///     Creates a localizer for a specific culture.
    /// </summary>
    IStringLocalizer WithCulture(string culture);
}