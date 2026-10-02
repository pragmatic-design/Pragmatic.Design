using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for <see cref="CurrencyCode" /> that serializes to/from the ISO 4217 code string.
/// </summary>
/// <remarks>
///     <para>
///         Serializes: <c>"USD"</c>, <c>"EUR"</c>, etc.
///     </para>
///     <para>
///         Deserializes: ISO 4217 three-letter codes (case-insensitive).
///     </para>
/// </remarks>
public sealed class CurrencyCodeJsonConverter : JsonConverter<CurrencyCode>
{
    /// <inheritdoc />
    public override CurrencyCode Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string for CurrencyCode, but got {reader.TokenType}");

        var code = reader.GetString();
        if (string.IsNullOrEmpty(code))
            throw new JsonException("CurrencyCode cannot be empty");

        if (!CurrencyCode.TryFromCode(code, out var currency))
            throw new JsonException($"'{code}' is not a valid ISO 4217 currency code");

        return currency;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CurrencyCode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Code);
    }
}

