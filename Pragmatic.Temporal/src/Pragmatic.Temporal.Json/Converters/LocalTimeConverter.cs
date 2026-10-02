using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for <see cref="LocalTime" />.
///     Serializes to/from ISO 8601 format (HH:mm:ss).
/// </summary>
public sealed class LocalTimeConverter : JsonConverter<LocalTime>
{
    /// <inheritdoc />
    public override LocalTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (LocalTime.TryParse(value, out var result))
            return result;
        throw new JsonException($"Unable to parse '{value}' as LocalTime.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>
///     JSON converter for nullable <see cref="LocalTime" />.
/// </summary>
public sealed class NullableLocalTimeConverter : JsonConverter<LocalTime?>
{
    private readonly LocalTimeConverter _inner = new();

    /// <inheritdoc />
    public override LocalTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return _inner.Read(ref reader, typeof(LocalTime), options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalTime? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            _inner.Write(writer, value.Value, options);
    }
}