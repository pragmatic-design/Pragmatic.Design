using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for <see cref="CultureCode" /> that serializes to/from the BCP 47 culture code string.
/// </summary>
/// <remarks>
///     <para>
///         Serializes: <c>"en"</c>, <c>"en-US"</c>, <c>"it-IT"</c>, etc.
///     </para>
///     <para>
///         Deserializes: BCP 47 language tags in the format <c>language</c> or <c>language-COUNTRY</c> (case-insensitive).
///     </para>
/// </remarks>
public sealed class CultureCodeJsonConverter : JsonConverter<CultureCode>
{
    /// <inheritdoc />
    public override CultureCode Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string for CultureCode, but got {reader.TokenType}");

        var code = reader.GetString();
        if (string.IsNullOrEmpty(code))
            throw new JsonException("CultureCode cannot be empty");

        if (!CultureCode.TryFromString(code, out var culture))
            throw new JsonException($"'{code}' is not a valid culture code");

        return culture;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CultureCode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Code);
    }
}
