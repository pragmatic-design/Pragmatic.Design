using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for <see cref="LanguageCode" /> that serializes to/from the ISO 639-1 code string.
/// </summary>
/// <remarks>
///     <para>
///         Serializes: <c>"en"</c>, <c>"it"</c>, etc.
///     </para>
///     <para>
///         Deserializes: ISO 639-1 two-letter codes (case-insensitive).
///     </para>
/// </remarks>
public sealed class LanguageCodeJsonConverter : JsonConverter<LanguageCode>
{
    /// <inheritdoc />
    public override LanguageCode Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string for LanguageCode, but got {reader.TokenType}");

        var code = reader.GetString();
        if (string.IsNullOrEmpty(code))
            throw new JsonException("LanguageCode cannot be empty");

        if (!LanguageCode.TryFromCode(code, out var language))
            throw new JsonException($"'{code}' is not a valid ISO 639-1 language code");

        return language;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LanguageCode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Code);
    }
}

