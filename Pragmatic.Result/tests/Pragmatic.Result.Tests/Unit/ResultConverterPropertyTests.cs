using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Result.Http;
using Pragmatic.Result.Serialization;
using Pragmatic.Testing.Assertions;
using Generated = Pragmatic.Result.Tests.Generated;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     The generated result converters on the body of a remote API's answer, the error a generated client reads
///     through <see cref="ErrorJson" /> (#158, #312). For any body, deserializing a result gives a result or a
///     <see cref="JsonException" />; anything else escapes the client as a fault of the caller's own.
/// </summary>
[Collection(ErrorTypeRegistryCollection.Name)]
public class ResultConverterPropertyTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new UntypedResultJsonConverter<int>(),
            new Generated.ResultInt32NotFoundErrorConflictErrorJsonConverter(),
            new Generated.ResultInt32BadRequestErrorNotFoundErrorConflictErrorJsonConverter(),
            new Generated.VoidResultNotFoundErrorConflictErrorJsonConverter(),
        },
    };

    private static readonly Type[] Types =
    [
        typeof(Result<int>), typeof(Result<int, NotFoundError, ConflictError>),
        typeof(Result<int, BadRequestError, NotFoundError, ConflictError>), typeof(VoidResult<NotFoundError, ConflictError>),
    ];

    // Stands for two bytes that are not UTF-8 inside a string: the text is built as a string, which cannot hold them.
    private const string Marker = "§";

    private static readonly string[] Keys =
        ["isSuccess", "ISSUCCESS", "value", "error", "$errorType", "message", "code", "resource", "id", "x", Marker];

    private static readonly string[] Scalars =
    [
        "true", "false", "null", "0", "1", "-1", "2147483648", "1.5", "1e400", "\"\"", "\"x\"", $"\"{Marker}\"",
        "\"Pragmatic.Result.Http.NotFoundError\"", "\"Pragmatic.Result.Http.ConflictError\"",
        "\"Pragmatic.Result.Http.BadRequestError\"", "\"System.String\"",
    ];

    // A failure whose error carries a discriminator, from the declared ones and others: a generic object reaches the
    // switch on the discriminator too rarely to test what each slot does with the rest of the error.
    private static readonly Gen<string> Shaped =
        from isSuccess in Gen.Elements("true", "false", "\"false\"", "0")
        from discriminator in Gen.Elements(Scalars)
        from members in Gen.Elements(Keys).Zip(Value(1)).Select(pair => $",\"{pair.Item1}\":{pair.Item2}").ListOf()
        select $"{{\"isSuccess\":{isSuccess},\"error\":{{\"$errorType\":{discriminator}{string.Concat(members)}}}}}";

    private static readonly Gen<string> Values = Gen.Frequency((2, Value(3)), (1, Shaped));

    private static Gen<string> Value(int depth)
    {
        var scalar = Gen.Elements(Scalars);
        if (depth == 0)
            return scalar;

        var inner = Value(depth - 1);
        return Gen.Frequency(
            (4, scalar),
            (1, inner.ListOf().Select(items => $"[{string.Join(",", items)}]")),
            (2, Gen.Elements(Keys).Zip(inner).Select(pair => $"\"{pair.Item1}\":{pair.Item2}")
                .ListOf().Select(members => $"{{{string.Join(",", members)}}}")));
    }

    [Fact]
    public void Any_json_of_the_format_s_vocabulary_deserializes_to_a_declared_outcome()
        => Prop.ForAll(Values.Select(Utf8).ToArbitrary(), Gen.Elements(Types).ToArbitrary(), IsDeclared)
            .Check(Config.QuickThrowOnFailure.WithMaxTest(2000));

    [Fact]
    public void Any_bytes_deserialize_to_a_declared_outcome()
        => Prop.ForAll(ArbMap.Default.ArbFor<byte[]>(), Gen.Elements(Types).ToArbitrary(), IsDeclared)
            .QuickCheckThrowOnFailure();

    /// <summary>
    ///     The generated converter reads the body as a <see cref="JsonDocument" /> and then its elements, whose
    ///     <c>GetBoolean</c>, <c>Name</c> and <c>GetString</c> throw <see cref="InvalidOperationException" /> on a
    ///     wrong kind or on bytes that are not UTF-8. They run inside the serializer, which rethrows them as
    ///     <see cref="JsonException" />; the grid adapters' 500 in #310 was the same read after binding, outside it.
    /// </summary>
    [Theory]
    [InlineData("{\"isSuccess\":1}")]
    [InlineData("{\"isSuccess\":false,\"error\":{\"$errorType\":\"§\"}}")]
    [InlineData("{\"§\":true}")]
    public void An_element_of_the_wrong_kind_or_not_utf8_is_refused_as_json(string json)
    {
        var act = () => JsonSerializer.Deserialize<Result<int, NotFoundError, ConflictError>>(Utf8(json), Options);

        act.Should().Throw<JsonException>();
    }

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
