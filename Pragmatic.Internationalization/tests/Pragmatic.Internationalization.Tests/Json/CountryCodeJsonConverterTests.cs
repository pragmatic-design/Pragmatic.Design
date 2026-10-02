using System.Text.Json;

using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.AspNetCore.Json.Converters;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Json;

/// <summary>
///     Tests for JSON serialization of CountryCode.
/// </summary>
public class CountryCodeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new CountryCodeJsonConverter() }
    };

    #region Serialize

    [Fact]
    public void Serialize_KnownCountry_WritesCode()
    {
        var country = CountryCode.US;

        var json = JsonSerializer.Serialize(country, Options);

        json.Should().Be("\"US\"");
    }

    [Fact]
    public void Serialize_InObject_SerializesAsString()
    {
        var dto = new TestDto { Country = CountryCode.Italy };

        var json = JsonSerializer.Serialize(dto, Options);

        json.Should().Contain("\"Country\":\"IT\"");
    }

    #endregion

    #region Deserialize

    [Fact]
    public void Deserialize_ValidCode_ReturnsCountryCode()
    {
        var json = "\"US\"";

        var country = JsonSerializer.Deserialize<CountryCode>(json, Options);

        country.Code.Should().Be("US");
        country.Name.Should().Be("United States");
    }

    [Fact]
    public void Deserialize_LowercaseCode_ReturnsCountryCode()
    {
        var json = "\"us\"";

        var country = JsonSerializer.Deserialize<CountryCode>(json, Options);

        country.Code.Should().Be("US");
    }

    [Fact]
    public void Deserialize_InObject_DeserializesFromString()
    {
        var json = """{"Country":"DE"}""";

        var dto = JsonSerializer.Deserialize<TestDto>(json, Options);

        dto.Should().NotBeNull();
        dto!.Country.Code.Should().Be("DE");
        dto.Country.Name.Should().Be("Germany");
    }

    [Fact]
    public void Deserialize_InvalidCode_ThrowsJsonException()
    {
        var json = "\"XX\"";

        var act = () => JsonSerializer.Deserialize<CountryCode>(json, Options);

        act.Should().Throw<JsonException>();
    }

    #endregion

    #region Roundtrip

    [Theory]
    [InlineData("US", "United States")]
    [InlineData("IT", "Italy")]
    [InlineData("DE", "Germany")]
    [InlineData("GB", "United Kingdom")]
    [InlineData("FR", "France")]
    [InlineData("JP", "Japan")]
    [InlineData("CH", "Switzerland")]
    public void Roundtrip_VariousCountries_PreservesValue(string code, string expectedName)
    {
        var original = CountryCode.FromCode(code);

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<CountryCode>(json, Options);

        deserialized.Should().Be(original);
        deserialized.Code.Should().Be(code);
        deserialized.Name.Should().Be(expectedName);
    }

    #endregion

    #region Integration with CurrencyCode

    [Fact]
    public void Deserialize_Country_HasCorrectDefaultCurrency()
    {
        var json = "\"IT\"";

        var country = JsonSerializer.Deserialize<CountryCode>(json, Options);

        country.DefaultCurrency.Should().Be(CurrencyCode.EUR);
    }

    [Fact]
    public void Deserialize_Country_HasCorrectDefaultLanguage()
    {
        var json = "\"IT\"";

        var country = JsonSerializer.Deserialize<CountryCode>(json, Options);

        country.DefaultLanguage.Should().Be(LanguageCode.Italian);
    }

    #endregion

    private record TestDto
    {
        public CountryCode Country { get; init; }
    }
}
