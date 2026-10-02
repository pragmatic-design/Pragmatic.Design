using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for nullable <see cref="Money" />.
/// </summary>
public sealed class NullableMoneyJsonConverter : JsonConverter<Money?>
{
    private readonly MoneyJsonConverter _innerConverter = new();

    /// <inheritdoc />
    public override Money? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return _innerConverter.Read(ref reader, typeof(Money), options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Money? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            _innerConverter.Write(writer, value.Value, options);
        else
            writer.WriteNullValue();
    }
}
