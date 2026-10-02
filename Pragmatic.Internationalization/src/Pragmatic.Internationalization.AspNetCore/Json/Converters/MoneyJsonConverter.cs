using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Json.Converters;

/// <summary>
///     JSON converter for <see cref="Money" /> that serializes to/from an object with amount and currency.
/// </summary>
/// <remarks>
///     <para>
///         Serializes to: <c>{ "amount": 99.99, "currency": "USD" }</c>
///     </para>
///     <para>
///         Deserializes from: Objects with "amount" (number) and "currency" (string) properties.
///     </para>
/// </remarks>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    private static readonly JsonEncodedText AmountProperty = JsonEncodedText.Encode("amount");
    private static readonly JsonEncodedText CurrencyProperty = JsonEncodedText.Encode("currency");

    /// <inheritdoc />
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected object for Money, but got {reader.TokenType}");

        decimal? amount = null;
        string? currencyCode = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected property name, but got {reader.TokenType}");

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, "amount", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var parsedAmount))
                    throw new JsonException($"Money 'amount' must be a JSON number, but got {reader.TokenType}");
                amount = parsedAmount;
            }
            else if (string.Equals(propertyName, "currency", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.TokenType != JsonTokenType.String)
                    throw new JsonException($"Money 'currency' must be a JSON string, but got {reader.TokenType}");
                currencyCode = reader.GetString();
            }
            else
            {
                reader.Skip(); // Ignore unknown properties, including nested objects/arrays
            }
        }

        if (!amount.HasValue)
            throw new JsonException("Money object must have an 'amount' property");

        if (string.IsNullOrEmpty(currencyCode))
            throw new JsonException("Money object must have a 'currency' property");

        if (!CurrencyCode.TryFromCode(currencyCode, out var currency))
            throw new JsonException($"'{currencyCode}' is not a valid ISO 4217 currency code");

        return Money.From(amount.Value, currency);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(AmountProperty, value.Amount);
        writer.WriteString(CurrencyProperty, value.Currency.Code);
        writer.WriteEndObject();
    }
}