using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.AspNetCore.Json.Converters;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Json;

public class NullableCurrencyCodeJsonConverterTests
{
    [Fact]
    public void Read_NullToken_ReturnsNull()
    {
        var result = JsonSerializer.Deserialize<CurrencyCode?>("null", CreateOptions());

        result.Should().BeNull();
    }

    [Fact]
    public void Read_ValidCode_ReturnsValue()
    {
        var result = JsonSerializer.Deserialize<CurrencyCode?>("\"USD\"", CreateOptions());

        result.Should().NotBeNull();
        result!.Value.Code.Should().Be("USD");
    }

    [Fact]
    public void Write_Value_SerializesToString()
    {
        CurrencyCode? value = CurrencyCode.USD;

        var json = JsonSerializer.Serialize(value, CreateOptions());

        json.Should().Be("\"USD\"");
    }

    [Fact]
    public void Read_RoundTripWithValue_PreservesValue()
    {
        var options = CreateOptions();
        CurrencyCode? original = CurrencyCode.USD;

        var json = JsonSerializer.Serialize(original, options);
        var result = JsonSerializer.Deserialize<CurrencyCode?>(json, options);

        result.Should().NotBeNull();
        result!.Value.Code.Should().Be("USD");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new NullableCurrencyCodeJsonConverter());
        return options;
    }
}
