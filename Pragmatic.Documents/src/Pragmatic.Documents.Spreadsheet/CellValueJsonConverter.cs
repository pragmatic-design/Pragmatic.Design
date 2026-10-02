using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Type-preserving JSON converter for <see cref="Cell.Value"/>. The value is weakly typed
/// (string, double, decimal, DateTime, bool, or null), so a plain <c>object</c> serialization would
/// lose the CLR type on round-trip — numbers and dates would come back as <see cref="JsonElement"/> or
/// strings. This converter writes a small tagged envelope (<c>{ "kind": ..., "value": ... }</c>) so the
/// documented type set survives a serialize/deserialize cycle. Integer/float CLR types are normalised to
/// <see cref="double"/> (XLSX has no integer type); anything outside the documented set throws.
/// </summary>
public sealed class CellValueJsonConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected a tagged cell-value object or null.");

        string? kind = null;
        object? value = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Malformed cell-value envelope.");

            var prop = reader.GetString();
            reader.Read();

            switch (prop)
            {
                case "kind":
                    kind = reader.GetString();
                    break;
                case "value":
                    // "kind" is always written before "value", so it is known here.
                    value = ReadTypedValue(ref reader, kind);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        switch (value)
        {
            case string s:
                writer.WriteString("kind", "string");
                writer.WriteString("value", s);
                break;
            case bool b:
                writer.WriteString("kind", "bool");
                writer.WriteBoolean("value", b);
                break;
            case decimal m:
                // Serialised as a string to preserve full decimal precision across round-trips.
                writer.WriteString("kind", "decimal");
                writer.WriteString("value", m.ToString(CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                writer.WriteString("kind", "dateTime");
                writer.WriteString("value", dt.ToString("O", CultureInfo.InvariantCulture));
                break;
            case double d:
                writer.WriteString("kind", "double");
                writer.WriteNumber("value", d);
                break;
            case float or int or long or short or byte or sbyte or uint or ulong or ushort:
                // XLSX has no integer type — normalise every integral/float value to double.
                writer.WriteString("kind", "double");
                writer.WriteNumber("value", Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            default:
                throw new JsonException(
                    $"Unsupported cell value type '{value.GetType()}'. Supported: string, double, decimal, DateTime, bool, null.");
        }
        writer.WriteEndObject();
    }

    private static object? ReadTypedValue(ref Utf8JsonReader reader, string? kind) => kind switch
    {
        "string" => reader.GetString(),
        "bool" => reader.GetBoolean(),
        "double" => reader.GetDouble(),
        "decimal" => decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture),
        "dateTime" => DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        _ => throw new JsonException($"Unknown or missing cell value kind '{kind}'."),
    };
}
