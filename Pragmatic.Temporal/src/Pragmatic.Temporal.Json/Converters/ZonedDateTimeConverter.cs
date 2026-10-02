using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for <see cref="ZonedDateTime" />.
///     Supports two formats:
///     - String: "2024-01-15T10:30:00+01:00[Europe/Rome]"
///     - Object: { "utc": "2024-01-15T09:30:00Z", "zone": "Europe/Rome", "local": "2024-01-15T10:30:00" }
/// </summary>
public sealed class ZonedDateTimeConverter : JsonConverter<ZonedDateTime>
{
    /// <summary>
    ///     Gets or sets whether to write as a string or object.
    ///     Default: true (string format)
    /// </summary>
    public bool WriteAsString { get; set; } = true;

    /// <inheritdoc />
    public override ZonedDateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // String format: "2024-01-15T10:30:00+01:00[Europe/Rome]"
            var value = reader.GetString();
            if (ZonedDateTime.TryParse(value, out var result))
                return result;
            throw new JsonException($"Unable to parse '{value}' as ZonedDateTime.");
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Object format: { "utc": "...", "zone": "..." }
            string? utcStr = null;
            string? zoneStr = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propName = reader.GetString();
                    reader.Read();

                    switch (propName?.ToLowerInvariant())
                    {
                        case "utc":
                            utcStr = reader.GetString();
                            break;
                        case "zone":
                        case "timezone":
                        case "timezoneid":
                            zoneStr = reader.GetString();
                            break;
                            // Ignore "local" - it's derived
                    }
                }
            }

            if (string.IsNullOrEmpty(utcStr))
                throw new JsonException("ZonedDateTime object must have 'utc' property.");
            if (string.IsNullOrEmpty(zoneStr))
                throw new JsonException("ZonedDateTime object must have 'zone' property.");

            if (!DateTimeOffset.TryParse(utcStr, out var utc))
                throw new JsonException($"Unable to parse '{utcStr}' as DateTimeOffset.");
            if (!TimeZoneResolver.TryGetTimeZone(zoneStr, out var zone) || zone is null)
                throw new JsonException($"Unknown timezone '{zoneStr}'.");

            return ZonedDateTime.FromUtc(utc.ToUniversalTime(), zone);
        }

        throw new JsonException($"Expected string or object for ZonedDateTime, got {reader.TokenType}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ZonedDateTime value, JsonSerializerOptions options)
    {
        if (WriteAsString)
        {
            // String format: "2024-01-15T10:30:00+01:00[Europe/Rome]"
            writer.WriteStringValue(value.ToString());
        }
        else
        {
            // Object format
            writer.WriteStartObject();
            writer.WriteString("utc", value.UtcDateTime.ToString("O"));
            writer.WriteString("zone", value.ZoneId);
            writer.WriteString("local", value.LocalDateTime.ToString("O"));
            writer.WriteEndObject();
        }
    }
}

/// <summary>
///     JSON converter for nullable <see cref="ZonedDateTime" />.
/// </summary>
public sealed class NullableZonedDateTimeConverter : JsonConverter<ZonedDateTime?>
{
    private readonly ZonedDateTimeConverter _inner = new();

    /// <inheritdoc />
    public override ZonedDateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return _inner.Read(ref reader, typeof(ZonedDateTime), options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ZonedDateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            _inner.Write(writer, value.Value, options);
    }
}