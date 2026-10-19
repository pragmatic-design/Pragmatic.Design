using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Temporal.Types;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Temporal.Json.Tests;

/// <summary>
///     The temporal converters on a request body (#158, #311). For any body, deserializing one of the types gives a
///     value or a <see cref="JsonException" />, the refusal ASP.NET Core answers with 400. Anything else is a 500.
/// </summary>
public class TemporalConverterPropertyTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().AddPragmaticTemporal();

    private static readonly Type[] Types =
    [
        typeof(LocalDate), typeof(LocalDate?), typeof(LocalTime), typeof(LocalTime?), typeof(LocalDateTime),
        typeof(LocalDateTime?), typeof(ZonedDateTime), typeof(ZonedDateTime?), typeof(Duration), typeof(Duration?),
        typeof(Period), typeof(Period?), typeof(DateRange), typeof(DateRange?), typeof(CronExpression),
    ];

    // The formats' own text, values just past what they accept, and the property names the object forms read.
    private static readonly string[] Strings =
    [
        "2024-01-01", "2023-12-31", "0001-01-01", "9999-12-31", "10:30:00", "2024-01-15T10:30:00",
        "2024-01-15T10:30:00+01:00[Europe/Rome]", "9999-12-31T23:59:59Z", "0001-01-01T00:00:00Z",
        "9999-12-31T23:59:59-14:00", "Europe/Rome", "Not/AZone", "UTC", "P1Y2M3D", "PT1H30M", "P2147483648Y",
        "P99999999999999999999D", "2024-01-01/2023-01-01", "2023-01-01/2024-01-01", "*/5 * * * *", "0 0 31 2 *", "",
        " ", "x", Marker,
    ];

    private static readonly string[] Keys = ["start", "end", "utc", "zone", "timezone", "timezoneid", "local", "x"];

    // Stands for two bytes that are not UTF-8 inside a string: the text is built as a string, which cannot hold them.
    private const string Marker = "§";

    private static readonly string[] Dates = ["2024-01-01", "2023-12-31", "0001-01-01", "9999-12-31", "x", Marker];

    private static readonly string[] Instants =
        ["2024-01-15T10:30:00Z", "9999-12-31T23:59:59Z", "9999-12-31T23:59:59-14:00", "0001-01-01T00:00:00+14:00", "x"];

    private static readonly string[] Zones = ["Europe/Rome", "Pacific/Kiritimati", "Etc/GMT+12", "UTC", "Not/AZone", "..", ""];

    // The object forms the converters read, each field from the values that matter to it: a generic object reaches
    // a start and an end in the wrong order, or an instant at the edge of the calendar in a zone, too rarely.
    private static readonly Gen<string> Shaped = Gen.OneOf(
        from start in Gen.Elements(Dates)
        from end in Gen.Elements(Dates)
        select $"{{\"start\":{Quoted(start)},\"end\":{Quoted(end)}}}",
        from utc in Gen.Elements(Instants)
        from zone in Gen.Elements(Zones)
        select $"{{\"utc\":{Quoted(utc)},\"zone\":{Quoted(zone)}}}");

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

    /// <summary>
    ///     A converter that calls <see cref="Utf8JsonReader.GetString" /> on bytes that are not UTF-8 gets an
    ///     <see cref="InvalidOperationException" />, which the serializer rethrows as a <see cref="JsonException" />.
    ///     The grid adapters' 500 in #310 came from a <see cref="JsonElement" /> read after binding, outside the
    ///     serializer, where nothing rethrows it.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryType))]
    public void A_string_that_is_not_utf8_is_refused_as_json(Type type)
    {
        byte[] body = [(byte)'"', 0xFF, 0xFE, (byte)'"'];

        var act = () => JsonSerializer.Deserialize(body, type, Options);

        act.Should().Throw<JsonException>();
    }

    public static TheoryData<Type> EveryType() => new(Types);

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
