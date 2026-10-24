using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Persistence.Sql.Conversion;

/// <summary>
///     A JSON column read as it is, without a type of its own (a payload, a document the application inspects): a <see cref="JsonDocument" />
///     parsed from the text and written back as its raw text, no serializer involved. The document is the caller's to dispose.
/// </summary>
public sealed class SqlJsonDocument : ISqlConverter<JsonDocument, string>
{
    public static string ToStore(JsonDocument value) => value.RootElement.GetRawText();

    public static JsonDocument FromStore(string value) => JsonDocument.Parse(value);
}
