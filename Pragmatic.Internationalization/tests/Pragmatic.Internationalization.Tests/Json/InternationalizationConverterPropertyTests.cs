using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Internationalization.AspNetCore.Json.Extensions;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Json;

/// <summary>
///     The internationalization converters on a request body (#158, #311). For any body, deserializing one of the
///     types gives a value or a <see cref="JsonException" />, the refusal ASP.NET Core answers with 400. Anything else
///     is a 500.
/// </summary>
public class InternationalizationConverterPropertyTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().AddPragmaticInternationalization();

    private static readonly Type[] Types =
    [
        typeof(Money), typeof(Money?), typeof(CurrencyCode), typeof(CurrencyCode?), typeof(LanguageCode),
        typeof(LanguageCode?), typeof(CountryCode), typeof(CountryCode?), typeof(CultureCode), typeof(CultureCode?),
        typeof(LocalizedString),
    ];

    // Codes that exist, codes one letter off, the separators a culture code splits on, and text no code has.
    private static readonly string[] Strings =
    [
        "EUR", "usd", "XXX", "EU", "EURO", "it", "IT", "en-US", "en-us", "zh-Hans-CN", "en-", "-US", "-", "--", "en--US",
        "it_IT", "", " ", "x", Marker,
    ];

    private static readonly string[] Keys = ["amount", "currency", "Amount", "CURRENCY", "en", "it-IT", "", "x", Marker];

    // Stands for two bytes that are not UTF-8 inside a string: the text is built as a string, which cannot hold them.
    private const string Marker = "§";

    // The object forms the converters read, each field from the values that matter to it.
    private static readonly Gen<string> Shaped = Gen.OneOf(
        from amount in Gen.Elements("0", "-1", "19.95", "79228162514264337593543950335", "79228162514264337593543950336",
            "1e400", "1e-400", "\"1\"", "null")
        from currency in Gen.Elements(Strings)
        select $"{{\"amount\":{amount},\"currency\":{Quoted(currency)}}}",
        Gen.Elements(Keys).Zip(Gen.Elements(Strings)).Select(pair => $"{Quoted(pair.Item1)}:{Quoted(pair.Item2)}")
            .ListOf().Select(members => $"{{{string.Join(",", members)}}}"));

    private static readonly Gen<string> Values = Gen.Frequency((3, Value(3)), (1, Shaped));

    private static Gen<string> Value(int depth)
    {
        var scalar = Gen.OneOf(
            Gen.Elements(Strings).Select(Quoted),
            Gen.Elements("0", "1", "-1", "1.5", "1e400", "99999999999999999999", "true", "false", "null"));

        if (depth == 0)
            return scalar;

        var inner = Value(depth - 1);
        return Gen.Frequency(
            (4, scalar),
            (1, inner.ListOf().Select(items => $"[{string.Join(",", items)}]")),
            (2, Gen.Elements(Keys).Zip(inner).Select(pair => $"{Quoted(pair.Item1)}:{pair.Item2}")
                .ListOf().Select(members => $"{{{string.Join(",", members)}}}")));
    }

    [Fact]
    public void Any_json_of_the_formats_vocabulary_deserializes_to_a_declared_outcome()
        => Prop.ForAll(Values.Select(Utf8).ToArbitrary(), Gen.Elements(Types).ToArbitrary(), IsDeclared)
            .Check(Config.QuickThrowOnFailure.WithMaxTest(2000));

    [Fact]
    public void Any_bytes_deserialize_to_a_declared_outcome()
        => Prop.ForAll(ArbMap.Default.ArbFor<byte[]>(), Gen.Elements(Types).ToArbitrary(), IsDeclared)
            .QuickCheckThrowOnFailure();

    private static bool IsDeclared(byte[] body, Type type)
    {
        try
        {
            JsonSerializer.Deserialize(body, type, Options);
        }
        catch (JsonException)
        {
        }

        return true;
    }

    // Not JsonSerializer.Serialize, which escapes the marker as §: the vocabulary holds no quote or backslash.
    private static string Quoted(string text) => $"\"{text}\"";

    private static byte[] Utf8(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        for (var i = 0; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == 0xC2 && bytes[i + 1] == 0xA7)
                (bytes[i], bytes[i + 1]) = ((byte)0xFF, (byte)0xFE);
        }

        return bytes;
    }
}
