using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for <see cref="CountryCode" /> that serializes to/from the ISO 3166-1 alpha-2 code string.
/// </summary>
/// <remarks>
///     <para>
///         Serializes: <c>"US"</c>, <c>"IT"</c>, etc.
///     </para>
///     <para>
///         Deserializes: ISO 3166-1 two-letter codes (case-insensitive).
///     </para>
/// </remarks>
public sealed class CountryCodeJsonConverter : JsonConverter<CountryCode>
{
    /// <inheritdoc />
    public override CountryCode Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string for CountryCode, but got {reader.TokenType}");

        var code = reader.GetString();
        if (string.IsNullOrEmpty(code))
            throw new JsonException("CountryCode cannot be empty");

        if (!CountryCode.TryFromCode(code, out var country))
            throw new JsonException($"'{code}' is not a valid ISO 3166-1 country code");

        return country;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CountryCode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Code);
    }
}
