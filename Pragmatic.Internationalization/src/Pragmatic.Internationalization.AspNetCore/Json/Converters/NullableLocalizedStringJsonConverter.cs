using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for nullable <see cref="LocalizedString" />.
/// </summary>
public sealed class NullableLocalizedStringJsonConverter : JsonConverter<LocalizedString?>
{
    private static readonly LocalizedStringJsonConverter Inner = new();

    /// <inheritdoc />
    public override LocalizedString? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return Inner.Read(ref reader, typeof(LocalizedString), options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalizedString? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        Inner.Write(writer, value, options);
    }
}
