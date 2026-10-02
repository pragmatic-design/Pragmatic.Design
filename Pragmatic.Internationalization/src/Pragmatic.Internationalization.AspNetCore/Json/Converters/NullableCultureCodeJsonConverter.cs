using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for nullable <see cref="CultureCode" />.
/// </summary>
public sealed class NullableCultureCodeJsonConverter : JsonConverter<CultureCode?>
{
    /// <inheritdoc />
    public override CultureCode? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string or null for CultureCode, but got {reader.TokenType}");

        var code = reader.GetString();
        if (string.IsNullOrEmpty(code))
            return null;

        if (!CultureCode.TryFromString(code, out var culture))
            throw new JsonException($"'{code}' is not a valid culture code");

        return culture;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CultureCode? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.Code);
        else
            writer.WriteNullValue();
    }
}
