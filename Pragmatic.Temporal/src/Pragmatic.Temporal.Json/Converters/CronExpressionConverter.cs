using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Converters;

/// <summary>
///     JSON converter for <see cref="CronExpression" />.
///     Serializes to/from the cron expression string.
/// </summary>
public sealed class CronExpressionConverter : JsonConverter<CronExpression>
{
    /// <inheritdoc />
    public override CronExpression? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException(
                "An empty string is not a valid CronExpression. Use a JSON null for optional values.");

        if (CronExpression.TryParse(value, out var result))
            return result;
        throw new JsonException($"Unable to parse '{value}' as CronExpression.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CronExpression value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Expression);
    }
}