namespace Pragmatic.Internationalization.Humanizer;

/// <summary>
///     Shared helper for extracting the base language code from a culture string.
/// </summary>
internal static class LanguageHelper
{
    /// <summary>
    ///     Extracts the language code from a culture string (e.g., "en-US" -> "en").
    /// </summary>
    internal static string GetLanguage(string culture)
    {
        var idx = culture.IndexOf('-');
        return idx > 0 ? culture[..idx].ToLowerInvariant() : culture.ToLowerInvariant();
    }
}
