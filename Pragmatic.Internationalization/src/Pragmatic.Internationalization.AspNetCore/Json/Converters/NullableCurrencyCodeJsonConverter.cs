using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for nullable <see cref="CurrencyCode" />.
/// </summary>
public sealed class NullableCurrencyCodeJsonConverter : JsonConverter<CurrencyCode?>
{
    /// <inheritdoc />
    public override CurrencyCode? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string or null for CurrencyCode, but got {reader.TokenType}");

        var code = reader.GetString();
        if (string.IsNullOrEmpty(code))
            return null;

        if (!CurrencyCode.TryFromCode(code, out var currency))
            throw new JsonException($"'{code}' is not a valid ISO 4217 currency code");

        return currency;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CurrencyCode? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.Code);
        else
            writer.WriteNullValue();
    }
}
