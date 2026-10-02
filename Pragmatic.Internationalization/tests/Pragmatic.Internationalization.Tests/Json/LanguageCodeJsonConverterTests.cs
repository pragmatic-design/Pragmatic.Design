using System.Text.Json;

using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.AspNetCore.Json.Converters;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Json;

/// <summary>
///     Tests for JSON serialization of LanguageCode.
/// </summary>
public class LanguageCodeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new LanguageCodeJsonConverter() }
    };

    #region Serialize

    [Fact]
    public void Serialize_KnownLanguage_WritesCode()
    {
        var language = LanguageCode.English;

        var json = JsonSerializer.Serialize(language, Options);

        json.Should().Be("\"en\"");
    }

    [Fact]
    public void Serialize_InObject_SerializesAsString()
    {
        var dto = new TestDto { Language = LanguageCode.Italian };

        var json = JsonSerializer.Serialize(dto, Options);

        json.Should().Contain("\"Language\":\"it\"");
    }

    #endregion

    #region Deserialize

    [Fact]
    public void Deserialize_ValidCode_ReturnsLanguageCode()
    {
        var json = "\"en\"";

        var language = JsonSerializer.Deserialize<LanguageCode>(json, Options);

        language.Code.Should().Be("en");
        language.Name.Should().Be("English");
    }

    [Fact]
    public void Deserialize_UppercaseCode_ReturnsLanguageCode()
    {
        var json = "\"EN\"";

        var language = JsonSerializer.Deserialize<LanguageCode>(json, Options);

        language.Code.Should().Be("en");
    }

    [Fact]
    public void Deserialize_InObject_DeserializesFromString()
    {
        var json = """{"Language":"de"}""";

        var dto = JsonSerializer.Deserialize<TestDto>(json, Options);

        dto.Should().NotBeNull();
        dto!.Language.Code.Should().Be("de");
        dto.Language.Name.Should().Be("German");
    }

    [Fact]
    public void Deserialize_InvalidCode_ThrowsJsonException()
    {
        var json = "\"invalid\"";

        var act = () => JsonSerializer.Deserialize<LanguageCode>(json, Options);

        act.Should().Throw<JsonException>();
    }

    #endregion

    #region Roundtrip

    [Theory]
    [InlineData("en", "English")]
    [InlineData("it", "Italian")]
    [InlineData("de", "German")]
    [InlineData("fr", "French")]
    [InlineData("es", "Spanish")]
    [InlineData("ja", "Japanese")]
    [InlineData("zh", "Chinese")]
    [InlineData("ar", "Arabic")]
    public void Roundtrip_VariousLanguages_PreservesValue(string code, string expectedName)
    {
        var original = LanguageCode.FromCode(code);

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<LanguageCode>(json, Options);

        deserialized.Should().Be(original);
        deserialized.Code.Should().Be(code);
        deserialized.Name.Should().Be(expectedName);
    }

    #endregion

    private record TestDto
    {
        public LanguageCode Language { get; init; }
    }
}
