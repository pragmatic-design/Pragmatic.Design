using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.AspNetCore.Json.Converters;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Json;

public class CurrencyCodeJsonConverterTests
{
    [Fact]
    public void Write_Currency_SerializesToString()
    {
        var json = JsonSerializer.Serialize(CurrencyCode.USD, CreateOptions());

        json.Should().Be("\"USD\"");
    }

    [Fact]
    public void Read_ValidCode_Deserializes()
    {
        var result = JsonSerializer.Deserialize<CurrencyCode>("\"EUR\"", CreateOptions());

        result.Code.Should().Be("EUR");
    }

    [Fact]
    public void Read_RoundTrip_PreservesValue()
    {
        var options = CreateOptions();

        var json = JsonSerializer.Serialize(CurrencyCode.USD, options);
        var result = JsonSerializer.Deserialize<CurrencyCode>(json, options);

        result.Code.Should().Be("USD");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new CurrencyCodeJsonConverter());
        return options;
    }
}
