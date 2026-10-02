using System.Text.Json;
using Pragmatic.Internationalization.AspNetCore.Json.Converters;

namespace Pragmatic.Internationalization.AspNetCore.Json.Extensions;

/// <summary>
///     Extension methods for configuring <see cref="JsonSerializerOptions" /> with internationalization converters.
/// </summary>
public static class JsonSerializerOptionsExtensions
{
    /// <summary>
    ///     Adds JSON converters for Pragmatic.Internationalization types
    ///     (Money, CurrencyCode, LanguageCode, CountryCode, CultureCode).
    /// </summary>
    /// <param name="options">The JSON serializer options.</param>
    /// <returns>The same options instance for chaining.</returns>
    /// <example>
    ///     <code>
    /// var options = new JsonSerializerOptions()
    ///     .AddPragmaticInternationalization();
    ///
    /// var json = JsonSerializer.Serialize(money, options);
    /// </code>
    /// </example>
    public static JsonSerializerOptions AddPragmaticInternationalization(this JsonSerializerOptions options)
    {
        Ensure.Ensure.ThrowIfNull(options);

        // Money converters
        options.Converters.Add(new MoneyJsonConverter());
        options.Converters.Add(new NullableMoneyJsonConverter());

        // Currency converters
        options.Converters.Add(new CurrencyCodeJsonConverter());
        options.Converters.Add(new NullableCurrencyCodeJsonConverter());

        // Language converters (ISO 639-1)
        options.Converters.Add(new LanguageCodeJsonConverter());
        options.Converters.Add(new NullableLanguageCodeJsonConverter());

        // Country converters (ISO 3166-1)
        options.Converters.Add(new CountryCodeJsonConverter());
        options.Converters.Add(new NullableCountryCodeJsonConverter());

        // Culture converters (BCP 47)
        options.Converters.Add(new CultureCodeJsonConverter());
        options.Converters.Add(new NullableCultureCodeJsonConverter());

        // LocalizedString converters (culture → value dictionary)
        options.Converters.Add(new LocalizedStringJsonConverter());
        options.Converters.Add(new NullableLocalizedStringJsonConverter());

        return options;
    }
}