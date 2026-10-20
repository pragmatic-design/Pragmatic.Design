using System.Text.Json;
using Pragmatic.Result.Http;
using Pragmatic.Result.Serialization;
using Generated = Pragmatic.Result.Fuzz.Generated;

namespace Pragmatic.Result.Fuzz;

/// <summary>
///     The body of a remote API's answer as a generated client reads it (#312): the generated converters, which read
///     the error through <see cref="ErrorJson" />. The first byte picks the result type, counted from <c>a</c>; the
///     rest is the body.
/// </summary>
/// <remarks>
///     The declared outcomes are a result and a <see cref="JsonException" />. Those return; anything else escapes
///     and is a crash. The converters are in this assembly, which is not instrumented: a crash in them is caught, but
///     the coverage that steers libFuzzer comes from <c>Pragmatic.Result.dll</c>.
/// </remarks>
public static class ResultConverterTarget
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new UntypedResultJsonConverter<int>(),
            new Generated.ResultInt32NotFoundErrorConflictErrorJsonConverter(),
            new Generated.ResultStringBadRequestErrorNotFoundErrorConflictErrorJsonConverter(),
            new Generated.VoidResultNotFoundErrorConflictErrorJsonConverter(),
        },
    };

    private static readonly Type[] Types =
    [
        typeof(Result<int>), typeof(Result<int, NotFoundError, ConflictError>),
        typeof(Result<string, BadRequestError, NotFoundError, ConflictError>), typeof(VoidResult<NotFoundError, ConflictError>),
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
