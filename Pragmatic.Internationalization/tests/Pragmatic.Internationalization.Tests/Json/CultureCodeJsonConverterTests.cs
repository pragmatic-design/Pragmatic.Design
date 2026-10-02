using System.Text.Json;

using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.AspNetCore.Json.Converters;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Json;

/// <summary>
///     Tests for JSON serialization of CultureCode.
/// </summary>
public class CultureCodeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new CultureCodeJsonConverter() }
    };

    #region Serialize

    [Fact]
    public void Serialize_LanguageOnlyCulture_WritesCode()
    {
        var culture = CultureCode.Italian;

        var json = JsonSerializer.Serialize(culture, Options);

        json.Should().Be("\"it\"");
    }

    [Fact]
    public void Serialize_LanguageAndCountryCulture_WritesFullCode()
    {
        var culture = CultureCode.EnglishUS;

        var json = JsonSerializer.Serialize(culture, Options);

        json.Should().Be("\"en-US\"");
    }

    [Fact]
    public void Serialize_InObject_SerializesAsString()
    {
        var dto = new TestDto { Culture = CultureCode.German };

        var json = JsonSerializer.Serialize(dto, Options);

        json.Should().Contain("\"Culture\":\"de\"");
    }

    #endregion

    #region Deserialize

    [Fact]
    public void Deserialize_LanguageOnlyCode_ReturnsCultureCode()
    {
        var json = "\"it\"";

        var culture = JsonSerializer.Deserialize<CultureCode>(json, Options);

        culture.Code.Should().Be("it");
        culture.Language.Should().Be(LanguageCode.Italian);
        culture.Country.Should().BeNull();
    }

    [Fact]
    public void Deserialize_LanguageAndCountryCode_ReturnsCultureCode()
    {
        var json = "\"en-US\"";

        var culture = JsonSerializer.Deserialize<CultureCode>(json, Options);

        culture.Code.Should().Be("en-US");
        culture.Language.Should().Be(LanguageCode.English);
        culture.Country.Should().Be(CountryCode.US);
    }

    [Fact]
    public void Deserialize_InObject_DeserializesFromString()
    {
        var json = """{"Culture":"de-CH"}""";

        var dto = JsonSerializer.Deserialize<TestDto>(json, Options);

        dto.Should().NotBeNull();
        dto!.Culture.Code.Should().Be("de-CH");
    }

    [Fact]
    public void Deserialize_InvalidCode_ThrowsJsonException()
    {
        var json = "\"invalid-culture\"";

        var act = () => JsonSerializer.Deserialize<CultureCode>(json, Options);

        act.Should().Throw<JsonException>();
    }

    #endregion

    #region Roundtrip

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("de-CH")]
    [InlineData("fr-CA")]
    public void Roundtrip_VariousCultures_PreservesValue(string code)
    {
        var original = CultureCode.FromString(code);

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<CultureCode>(json, Options);

        deserialized.Should().Be(original);
        deserialized.Code.Should().Be(code);
    }

    #endregion

    private record TestDto
    {
        public CultureCode Culture { get; init; }
    }
}
