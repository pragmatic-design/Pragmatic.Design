using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for <see cref="Duration" />.
///     Serializes to/from ISO 8601 duration format (e.g., "PT1H30M").
/// </summary>
public sealed class DurationConverter : JsonConverter<Duration>
{
    /// <inheritdoc />
    public override Duration Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            throw new JsonException("Cannot deserialize null JSON token as non-nullable Duration.");

        var value = reader.GetString();
        if (value is not null && Duration.TryParse(value, out var result))
            return result;
        throw new JsonException($"Unable to parse '{value}' as Duration.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Duration value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>
///     JSON converter for nullable <see cref="Duration" />.
/// </summary>
public sealed class NullableDurationConverter : JsonConverter<Duration?>
{
    private readonly DurationConverter _inner = new();

    /// <inheritdoc />
    public override Duration? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return _inner.Read(ref reader, typeof(Duration), options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Duration? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            _inner.Write(writer, value.Value, options);
    }
}