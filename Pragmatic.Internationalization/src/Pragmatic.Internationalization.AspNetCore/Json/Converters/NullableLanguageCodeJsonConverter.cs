using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for nullable <see cref="LanguageCode" />.
/// </summary>
public sealed class NullableLanguageCodeJsonConverter : JsonConverter<LanguageCode?>
{
    /// <inheritdoc />
    public override LanguageCode? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string or null for LanguageCode, but got {reader.TokenType}");

        var code = reader.GetString();
        if (string.IsNullOrEmpty(code))
            return null;

        if (!LanguageCode.TryFromCode(code, out var language))
            throw new JsonException($"'{code}' is not a valid ISO 639-1 language code");

        return language;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LanguageCode? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.Code);
        else
            writer.WriteNullValue();
    }
}
