using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for <see cref="LocalDateTime" />.
///     Serializes to/from ISO 8601 format (yyyy-MM-ddTHH:mm:ss).
///     Note: Timezone information in input is stripped (this is a local datetime).
/// </summary>
public sealed class LocalDateTimeConverter : JsonConverter<LocalDateTime>
{
    /// <inheritdoc />
    public override LocalDateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (LocalDateTime.TryParse(value, out var result))
            return result;
        throw new JsonException($"Unable to parse '{value}' as LocalDateTime.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalDateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>
///     JSON converter for nullable <see cref="LocalDateTime" />.
/// </summary>
public sealed class NullableLocalDateTimeConverter : JsonConverter<LocalDateTime?>
{
    private readonly LocalDateTimeConverter _inner = new();

    /// <inheritdoc />
    public override LocalDateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return _inner.Read(ref reader, typeof(LocalDateTime), options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalDateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            _inner.Write(writer, value.Value, options);
    }
}