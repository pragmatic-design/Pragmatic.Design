using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Parsing / factory members of <see cref="CultureCode"/>: building a culture from a string,
///     a <see cref="CultureInfo"/>, or an Accept-Language header.
/// </summary>
public readonly partial struct CultureCode
{
    /// <summary>
    ///     Creates a CultureCode from a culture string.
    /// </summary>
    /// <param name="code">The culture code (e.g., "en-US", "it", "de-CH").</param>
    /// <returns>The corresponding CultureCode.</returns>
    /// <exception cref="ArgumentException">Thrown if the code is not valid.</exception>
    public static CultureCode FromString(string code)
    {
        if (!TryFromString(code, out var culture))
            throw new ArgumentException($"'{code}' is not a valid culture code.", nameof(code));
        return culture;
    }

    /// <summary>
    ///     Attempts to create a CultureCode from a culture string.
    /// </summary>
    /// <param name="code">The culture code (e.g., "en-US", "it", "de-CH").</param>
    /// <param name="culture">When successful, contains the CultureCode; otherwise, default.</param>
    /// <returns>True if the code is valid; otherwise, false.</returns>
    public static bool TryFromString(string? code, [NotNullWhen(true)] out CultureCode culture)
    {
        culture = default;

        if (string.IsNullOrWhiteSpace(code))
            return false;

        var parts = code.Split('-');

        // Language only: "en", "it"
        if (parts.Length == 1)
        {
            if (!LanguageCode.TryFromCode(parts[0], out var language))
                return false;

            culture = new CultureCode(language);
            return true;
        }

        // Language + Country: "en-US", "de-CH"
        if (parts.Length == 2)
        {
            if (!LanguageCode.TryFromCode(parts[0], out var language))
                return false;

            if (!CountryCode.TryFromCode(parts[1], out var country))
                return false;

            culture = new CultureCode(language, country);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Creates a CultureCode from a .NET CultureInfo.
    /// </summary>
    /// <param name="cultureInfo">The CultureInfo to convert.</param>
    /// <returns>The corresponding CultureCode.</returns>
    public static CultureCode FromCultureInfo(CultureInfo cultureInfo)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);

        // InvariantCulture has an empty Name — fall back to English
        if (string.IsNullOrEmpty(cultureInfo.Name))
            return new CultureCode(LanguageCode.English);

        if (TryFromString(cultureInfo.Name, out var culture))
            return culture;

        // Fallback: try just the language
        if (LanguageCode.TryFromCode(cultureInfo.TwoLetterISOLanguageName, out var language))
            return new CultureCode(language);

        throw new ArgumentException($"Cannot convert CultureInfo '{cultureInfo.Name}' to CultureCode.", nameof(cultureInfo));
    }

    /// <summary>
    ///     Parses the Accept-Language HTTP header and returns the first valid culture.
    /// </summary>
    /// <param name="acceptLanguageHeader">The Accept-Language header value.</param>
    /// <returns>The first valid CultureCode from the header, or null if none found.</returns>
    /// <remarks>
    ///     This method ignores quality values (q=...) and returns the first valid culture
    ///     in declaration order. For full Accept-Language parsing with q-value priority,
    ///     use <c>I18NContextMiddleware</c> from Pragmatic.Internationalization.AspNetCore instead.
    /// </remarks>
    public static CultureCode? FromAcceptLanguage(string? acceptLanguageHeader)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguageHeader))
            return null;

        // Parse "en-US, en;q=0.9, it;q=0.8" format
        var languages = acceptLanguageHeader
            .Split(',')
            .Select(s => s.Trim().Split(';')[0].Trim())
            .Where(s => !string.IsNullOrEmpty(s));

        foreach (var lang in languages)
        {
            if (TryFromString(lang, out var culture))
                return culture;
        }

        return null;
    }

    /// <summary>
    ///     Checks if a string is a valid culture code.
    /// </summary>
    /// <param name="code">The code to check.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    public static bool IsValid([NotNullWhen(true)] string? code)
    {
        return TryFromString(code, out _);
    }
}
