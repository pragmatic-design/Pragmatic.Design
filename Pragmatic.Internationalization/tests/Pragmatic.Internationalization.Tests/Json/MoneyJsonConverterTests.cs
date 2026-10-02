using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.AspNetCore.Json.Converters;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Json;

public class MoneyJsonConverterTests
{
    [Fact]
    public void Write_Money_SerializesAmountAndCurrency()
    {
        var json = JsonSerializer.Serialize(Money.From(19.95m, CurrencyCode.USD), CreateOptions());

        json.Should().Be("{\"amount\":19.95,\"currency\":\"USD\"}");
    }

    [Fact]
    public void Read_ValidObject_DeserializesAmountAndCurrency()
    {
        var json = "{\"amount\":42.00,\"currency\":\"EUR\"}";

        var result = JsonSerializer.Deserialize<Money>(json, CreateOptions());

        result.Amount.Should().Be(42.00m);
        result.Currency.Code.Should().Be("EUR");
    }

    [Fact]
    public void Read_PropertyNamesCaseInsensitive_DeserializesCorrectly()
    {
        var json = "{\"Amount\":5,\"Currency\":\"USD\"}";

        var result = JsonSerializer.Deserialize<Money>(json, CreateOptions());

        result.Amount.Should().Be(5m);
        result.Currency.Code.Should().Be("USD");
    }

    [Fact]
    public void Read_RoundTrip_PreservesValue()
    {
        var original = Money.From(1234.56m, CurrencyCode.USD);
        var options = CreateOptions();

        var json = JsonSerializer.Serialize(original, options);
        var result = JsonSerializer.Deserialize<Money>(json, options);

        result.Should().Be(original);
    }

    [Fact]
    public void Read_MissingCurrency_ThrowsJsonException()
    {
        var json = "{\"amount\":10}";

        var act = () => JsonSerializer.Deserialize<Money>(json, CreateOptions());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_MissingAmount_ThrowsJsonException()
    {
        var json = "{\"currency\":\"USD\"}";

        var act = () => JsonSerializer.Deserialize<Money>(json, CreateOptions());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_NotAnObject_ThrowsJsonException()
    {
        var json = "\"USD\"";

        var act = () => JsonSerializer.Deserialize<Money>(json, CreateOptions());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_AmountAsString_ThrowsJsonException()
    {
        // Guard #15: a wrong-typed token must surface as JsonException (→ HTTP 400), not
        // an unhandled InvalidOperationException (→ HTTP 500).
        var json = "{\"amount\":\"abc\",\"currency\":\"USD\"}";

        var act = () => JsonSerializer.Deserialize<Money>(json, CreateOptions());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_CurrencyAsNumber_ThrowsJsonException()
    {
        var json = "{\"amount\":10,\"currency\":978}";

        var act = () => JsonSerializer.Deserialize<Money>(json, CreateOptions());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_UnknownObjectProperty_IsIgnored()
    {
        var json = "{\"amount\":10,\"meta\":{\"note\":\"x\"},\"currency\":\"USD\"}";

        var result = JsonSerializer.Deserialize<Money>(json, CreateOptions());

        result.Amount.Should().Be(10m);
        result.Currency.Code.Should().Be("USD");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new MoneyJsonConverter());
        return options;
    }
}
