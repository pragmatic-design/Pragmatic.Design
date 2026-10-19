using System.Text.Json;
using Pragmatic.Internationalization.AspNetCore.Json.Extensions;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Fuzz;

/// <summary>
///     The internationalization converters on a request body (#311). The first byte picks the type, counted from
///     <c>a</c> so a seed reads as what it is (<c>a{"amount":…}</c> is a <see cref="Money" />); the rest is the body.
/// </summary>
/// <remarks>
///     The declared outcomes are a value and a <see cref="JsonException" />, which ASP.NET Core answers with 400.
///     Those return; anything else escapes and is a crash.
/// </remarks>
public static class InternationalizationJsonTarget
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().AddPragmaticInternationalization();

    private static readonly Type[] Types =
    [
        typeof(Money), typeof(Money?), typeof(CurrencyCode), typeof(CurrencyCode?), typeof(LanguageCode),
        typeof(LanguageCode?), typeof(CountryCode), typeof(CountryCode?), typeof(CultureCode), typeof(CultureCode?),
        typeof(LocalizedString),
    ];

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var type = Types[((data[0] - 'a') % Types.Length + Types.Length) % Types.Length];
        try
        {
            JsonSerializer.Deserialize(data[1..], type, Options);
        }
        catch (JsonException)
        {
        }
    }
}
