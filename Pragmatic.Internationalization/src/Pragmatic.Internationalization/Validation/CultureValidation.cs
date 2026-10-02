using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Validation;

/// <summary>
///     Validation helpers for culture-related input.
/// </summary>
/// <remarks>
///     Use these methods to validate user input (query strings, form data, etc.)
///     before setting cultures or storing preferences.
/// </remarks>
public static class CultureValidation
{
    /// <summary>
    ///     Validates that a culture code string is valid.
    /// </summary>
    /// <param name="input">The culture code to validate.</param>
    /// <param name="culture">When successful, contains the parsed CultureCode.</param>
    /// <param name="errorMessage">When failed, contains an error message.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    /// <example>
    /// <code>
    /// if (CultureValidation.TryValidate(request.Lang, out var culture, out var error))
    /// {
    ///     I18N.SetCulture(culture);
    /// }
    /// else
    /// {
    ///     return BadRequest(error);
    /// }
    /// </code>
    /// </example>
    public static bool TryValidate(
        string? input,
        out CultureCode culture,
        out string? errorMessage)
    {
        culture = default;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Culture code is required.";
            return false;
        }

        if (!CultureCode.TryFromString(input, out culture))
        {
            errorMessage = $"'{input}' is not a valid culture code.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Validates that a culture code is valid and supported.
    /// </summary>
    /// <param name="input">The culture code to validate.</param>
    /// <param name="resolver">The config resolver to check supported cultures.</param>
    /// <param name="culture">When successful, contains the parsed CultureCode.</param>
    /// <param name="errorMessage">When failed, contains an error message.</param>
    /// <returns>True if valid and supported; otherwise, false.</returns>
    /// <example>
    /// <code>
    /// if (CultureValidation.TryValidateSupported(request.Lang, _resolver, out var culture, out var error))
    /// {
    ///     I18N.SetCulture(culture);
    /// }
    /// </code>
    /// </example>
    public static bool TryValidateSupported(
        string? input,
        I18NConfigResolver resolver,
        out CultureCode culture,
        out string? errorMessage)
    {
        if (!TryValidate(input, out culture, out errorMessage))
            return false;

        if (!resolver.IsCultureSupported(culture))
        {
            errorMessage = $"Culture '{input}' is not supported.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Validates that a language code string is valid.
    /// </summary>
    /// <param name="input">The language code to validate.</param>
    /// <param name="language">When successful, contains the parsed LanguageCode.</param>
    /// <param name="errorMessage">When failed, contains an error message.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    public static bool TryValidateLanguage(
        string? input,
        out LanguageCode language,
        out string? errorMessage)
    {
        language = default;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Language code is required.";
            return false;
        }

        if (!LanguageCode.TryFromCode(input, out language))
        {
            errorMessage = $"'{input}' is not a valid ISO 639-1 language code.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Validates that a country code string is valid.
    /// </summary>
    /// <param name="input">The country code to validate.</param>
    /// <param name="country">When successful, contains the parsed CountryCode.</param>
    /// <param name="errorMessage">When failed, contains an error message.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    public static bool TryValidateCountry(
        string? input,
        out CountryCode country,
        out string? errorMessage)
    {
        country = default;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Country code is required.";
            return false;
        }

        if (!CountryCode.TryFromCode(input, out country))
        {
            errorMessage = $"'{input}' is not a valid ISO 3166-1 country code.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Validates that a currency code string is valid.
    /// </summary>
    /// <param name="input">The currency code to validate.</param>
    /// <param name="currency">When successful, contains the parsed CurrencyCode.</param>
    /// <param name="errorMessage">When failed, contains an error message.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    public static bool TryValidateCurrency(
        string? input,
        out CurrencyCode currency,
        out string? errorMessage)
    {
        currency = default;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Currency code is required.";
            return false;
        }

        if (!CurrencyCode.TryFromCode(input, out currency))
        {
            errorMessage = $"'{input}' is not a valid ISO 4217 currency code.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Gets the best matching culture from a list of candidates.
    /// </summary>
    /// <param name="candidates">The culture codes to try, in priority order.</param>
    /// <param name="resolver">The config resolver to check supported cultures.</param>
    /// <returns>The first supported culture, or null if none match.</returns>
    /// <example>
    /// <code>
    /// // From Accept-Language header
    /// var candidates = new[] { "it-IT", "it", "en-US", "en" };
    /// var best = CultureValidation.GetBestMatch(candidates, _resolver);
    /// if (best.HasValue)
    ///     I18N.SetCulture(best.Value);
    /// </code>
    /// </example>
    public static CultureCode? GetBestMatch(
        IEnumerable<string> candidates,
        I18NConfigResolver resolver)
    {
        foreach (var candidate in candidates)
        {
            if (!CultureCode.TryFromString(candidate, out var culture))
                continue;

            var best = resolver.FindBestMatch(culture);
            if (best.HasValue)
                return best;
        }

        return null;
    }

    /// <summary>
    ///     Parses an Accept-Language header and returns the best supported culture.
    /// </summary>
    /// <param name="acceptLanguageHeader">The Accept-Language header value.</param>
    /// <param name="resolver">The config resolver to check supported cultures.</param>
    /// <returns>The best supported culture, or null if none match.</returns>
    public static CultureCode? GetBestMatchFromAcceptLanguage(
        string? acceptLanguageHeader,
        I18NConfigResolver resolver)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguageHeader))
            return null;

        // Parse "en-US, en;q=0.9, it;q=0.8" format
        // Extract culture codes in order of preference (ignoring q values for now)
        var candidates = acceptLanguageHeader
            .Split(',')
            .Select(s => s.Trim().Split(';')[0].Trim())
            .Where(s => !string.IsNullOrEmpty(s));

        return GetBestMatch(candidates, resolver);
    }
}
