using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Patch.Serialization;
using Xunit;

namespace Pragmatic.Patch.Tests.Unit;

/// <summary>
///     <see cref="OptionalConverter{T}" /> on a PATCH body (#158, #311). For any body, deserializing an
///     <see cref="Optional{T}" /> gives a value or a <see cref="JsonException" />, the refusal ASP.NET Core answers with
///     400; whatever the inner type's converter refuses has to surface the same way.
/// </summary>
public class OptionalConverterPropertyTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new OptionalConverter<int>(), new OptionalConverter<string>(), new OptionalConverter<decimal>(),
            new OptionalConverter<Guid>(), new OptionalConverter<DayOfWeek>(), new OptionalConverter<int[]>(),
            new OptionalConverter<Dictionary<string, int>>(),
        },
    };

    private static readonly Type[] Types =
    [
        typeof(Optional<int>), typeof(Optional<string>), typeof(Optional<decimal>), typeof(Optional<Guid>),
        typeof(Optional<DayOfWeek>), typeof(Optional<int[]>), typeof(Optional<Dictionary<string, int>>),
    ];

    private static readonly string[] Scalars =
    [
        "0", "-1", "2147483648", "1.5", "1e400", "79228162514264337593543950336", "true", "null", "\"\"", "\"x\"",
        "\"3fa85f64-5717-4562-b3fc-2c963f66afa6\"", "\"Monday\"", "\"§\"",
    ];

    private static readonly Gen<string> Values = Value(3);

    private static Gen<string> Value(int depth)
    {
        var scalar = Gen.Elements(Scalars);
        if (depth == 0)
            return scalar;

        var inner = Value(depth - 1);
        return Gen.Frequency(
            (4, scalar),
            (1, inner.ListOf().Select(items => $"[{string.Join(",", items)}]")),
            (1, Gen.Elements("\"a\"", "\"\"", "\"§\"").Zip(inner).Select(pair => $"{pair.Item1}:{pair.Item2}")
                .ListOf().Select(members => $"{{{string.Join(",", members)}}}")));
    }

    [Fact]
    public void Any_json_deserializes_to_a_declared_outcome()
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

    // U+00A7 stands for two bytes that are not UTF-8 inside a string, which a string cannot hold.
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
