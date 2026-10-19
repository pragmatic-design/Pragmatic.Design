using System.Text.Json;
using Pragmatic.Patch.Serialization;

namespace Pragmatic.Patch.Fuzz;

/// <summary>
///     <see cref="OptionalConverter{T}" /> on a PATCH body (#311). The first byte picks the inner type, counted from
///     <c>a</c> (<c>a</c> is <see cref="int" />, <c>b</c> <see cref="string" />); the rest is the body.
/// </summary>
/// <remarks>
///     The declared outcomes are a value and a <see cref="JsonException" />, which ASP.NET Core answers with 400.
///     Those return; anything else escapes and is a crash.
/// </remarks>
public static class OptionalConverterTarget
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
