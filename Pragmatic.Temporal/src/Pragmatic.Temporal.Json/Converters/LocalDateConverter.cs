using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for <see cref="LocalDate" />.
///     Serializes to/from ISO 8601 format (yyyy-MM-dd).
/// </summary>
public sealed class LocalDateConverter : JsonConverter<LocalDate>
{
    /// <inheritdoc />
    public override LocalDate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (LocalDate.TryParse(value, out var result))
            return result;
        throw new JsonException($"Unable to parse '{value}' as LocalDate.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalDate value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

