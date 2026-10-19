using System.Text.Json;
using Pragmatic.Temporal.Json;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Fuzz;

/// <summary>
///     The temporal converters on a request body (#311). The first byte picks the type, counted from <c>a</c> so a
///     seed reads as what it is (<c>m{"start":…}</c> is a <see cref="DateRange" />); the rest is the body.
/// </summary>
/// <remarks>
///     The declared outcomes are a value and a <see cref="JsonException" />, which ASP.NET Core answers with 400.
///     Those return; anything else escapes and is a crash.
/// </remarks>
public static class TemporalJsonTarget
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().AddPragmaticTemporal();

    private static readonly Type[] Types =
    [
        typeof(LocalDate), typeof(LocalDate?), typeof(LocalTime), typeof(LocalTime?), typeof(LocalDateTime),
        typeof(LocalDateTime?), typeof(ZonedDateTime), typeof(ZonedDateTime?), typeof(Duration), typeof(Duration?),
        typeof(Period), typeof(Period?), typeof(DateRange), typeof(DateRange?), typeof(CronExpression),
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
