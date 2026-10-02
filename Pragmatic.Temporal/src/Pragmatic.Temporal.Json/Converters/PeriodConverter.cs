using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for <see cref="Period" />.
///     Serializes as ISO 8601 duration string: "P1Y2M3D".
/// </summary>
/// <example>
///     <code>
///     // Serialized formats:
///     "P1Y"      // 1 year
///     "P2M"      // 2 months
///     "P15D"     // 15 days
///     "P1Y2M3D"  // 1 year, 2 months, 3 days
///     </code>
/// </example>
public sealed class PeriodConverter : JsonConverter<Period>
{
    public override Period Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            throw new JsonException("Cannot deserialize null JSON token as non-nullable Period.");

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string for Period, got {reader.TokenType}.");

        var str = reader.GetString();
        if (Period.TryParse(str, out var result))
            return result;

        throw new JsonException($"Invalid Period format: '{str}'. Expected ISO 8601 format like 'P1Y2M3D'.");
    }

    public override void Write(Utf8JsonWriter writer, Period value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

