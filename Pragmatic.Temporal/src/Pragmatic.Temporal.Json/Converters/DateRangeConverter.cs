using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for <see cref="DateRange" />.
///     Serializes as an object with "start" and "end" properties.
/// </summary>
/// <example>
///     <code>
///     // Serialized format:
///     {"start":"2024-01-01","end":"2024-03-31"}
///     </code>
/// </example>
public sealed class DateRangeConverter : JsonConverter<DateRange>
{
    public override DateRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return DateRange.Empty;

        // Support string format: "2024-01-01/2024-03-31"
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (DateRange.TryParse(str, out var result))
                return result;
            throw new JsonException($"Invalid DateRange format: '{str}'. Expected 'yyyy-MM-dd/yyyy-MM-dd'.");
        }

        // Object format: {"start":"2024-01-01","end":"2024-03-31"}
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected object or string for DateRange, got {reader.TokenType}.");

        LocalDate? start = null;
        LocalDate? end = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected property name.");

            var propertyName = reader.GetString()?.ToLowerInvariant();
            reader.Read();

            switch (propertyName)
            {
                case "start":
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        var startStr = reader.GetString();
                        if (LocalDate.TryParse(startStr, out var s))
                            start = s;
                        else
                            throw new JsonException($"Invalid start date: '{startStr}'.");
                    }
                    break;

                case "end":
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        var endStr = reader.GetString();
                        if (LocalDate.TryParse(endStr, out var e))
                            end = e;
                        else
                            throw new JsonException($"Invalid end date: '{endStr}'.");
                    }
                    break;
            }
        }

        if (!start.HasValue || !end.HasValue)
            throw new JsonException("DateRange requires both 'start' and 'end' properties.");

        return new DateRange(start.Value, end.Value);
    }

    public override void Write(Utf8JsonWriter writer, DateRange value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("start", value.Start.ToString());
        writer.WriteString("end", value.End.ToString());
        writer.WriteEndObject();
    }
}

/// <summary>
///     JSON converter for nullable <see cref="DateRange" />.
/// </summary>
public sealed class NullableDateRangeConverter : JsonConverter<DateRange?>
{
    private readonly DateRangeConverter _inner = new();

    public override DateRange? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return _inner.Read(ref reader, typeof(DateRange), options);
    }

    public override void Write(Utf8JsonWriter writer, DateRange? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            _inner.Write(writer, value.Value, options);
        else
            writer.WriteNullValue();
    }
}
