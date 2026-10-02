using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for nullable <see cref="Period" />.
/// </summary>
public sealed class NullablePeriodConverter : JsonConverter<Period?>
{
    private readonly PeriodConverter _inner = new();

    /// <inheritdoc />
    public override Period? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return _inner.Read(ref reader, typeof(Period), options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Period? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            _inner.Write(writer, value.Value, options);
        else
            writer.WriteNullValue();
    }
}
