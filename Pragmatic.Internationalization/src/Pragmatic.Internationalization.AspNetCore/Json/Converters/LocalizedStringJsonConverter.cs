using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for <see cref="LocalizedString" /> that serializes as a culture-value dictionary.
/// </summary>
/// <remarks>
///     <para>
///         Serializes to: <c>{ "en": "Hello", "it": "Ciao" }</c>
///     </para>
///     <para>
///         Use this when the DTO exposes <see cref="LocalizedString"/> directly (e.g., admin/edit DTOs).
///         When the SG mapping converts <see cref="LocalizedString"/> to <c>string</c>, this converter
///         is not involved — the culture resolution happens at mapping time.
///     </para>
/// </remarks>
public sealed class LocalizedStringJsonConverter : JsonConverter<LocalizedString>
{
    /// <inheritdoc />
    public override LocalizedString? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected object for LocalizedString, but got {reader.TokenType}");

        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected property name (culture code), but got {reader.TokenType}");

            var culture = reader.GetString()!;
            reader.Read();

            if (reader.TokenType == JsonTokenType.Null)
                continue;

            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException(
                    $"LocalizedString value for culture '{culture}' must be a string, but got {reader.TokenType}");

            translations[culture] = reader.GetString()!;
        }

        // An empty object "{}" is a legitimate "no translations yet" state,
        // not a missing value. Collapsing it to null would break DTOs that
        // expose LocalizedString (non-nullable) for admin/edit flows. Reserve
        // null for an explicit JSON null token. Always return a FRESH instance:
        // LocalizedString is mutable and the caller may write into the result.
        return LocalizedString.From(translations);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalizedString value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (culture, text) in value)
        {
            writer.WriteString(culture, text);
        }

        writer.WriteEndObject();
    }
}
