using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for nullable <see cref="CountryCode" />.
/// </summary>
public sealed class NullableCountryCodeJsonConverter : JsonConverter<CountryCode?>
{
    /// <inheritdoc />
    public override CountryCode? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string or null for CountryCode, but got {reader.TokenType}");

        var code = reader.GetString();
        if (string.IsNullOrEmpty(code))
            return null;

        if (!CountryCode.TryFromCode(code, out var country))
            throw new JsonException($"'{code}' is not a valid ISO 3166-1 country code");

        return country;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CountryCode? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.Code);
        else
            writer.WriteNullValue();
    }
}
