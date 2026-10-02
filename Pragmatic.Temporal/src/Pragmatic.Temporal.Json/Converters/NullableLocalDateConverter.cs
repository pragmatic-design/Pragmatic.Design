using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for nullable <see cref="LocalDate" />.
/// </summary>
public sealed class NullableLocalDateConverter : JsonConverter<LocalDate?>
{
    private readonly LocalDateConverter _inner = new();

    /// <inheritdoc />
    public override LocalDate? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return _inner.Read(ref reader, typeof(LocalDate), options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalDate? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            _inner.Write(writer, value.Value, options);
    }
}
