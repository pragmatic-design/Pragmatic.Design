using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Types;

/// <summary>
///     Tests for the LanguageCode type (generated from ISO 639-1).
/// </summary>
public class LanguageCodeTests
{
    #region Static Properties

    [Fact]
    public void English_HasCorrectProperties()
    {
        var lang = LanguageCode.English;

        lang.Code.Should().Be("en");
        lang.Name.Should().Be("English");
        lang.NativeName.Should().Be("English");
        lang.IsRightToLeft.Should().BeFalse();
        lang.PluralFamily.Should().Be(PluralRuleFamily.Germanic);
    }

    [Fact]
    public void Italian_HasCorrectProperties()
    {
        var lang = LanguageCode.Italian;

        lang.Code.Should().Be("it");
        lang.Name.Should().Be("Italian");
        lang.NativeName.Should().Be("Italiano");
        lang.IsRightToLeft.Should().BeFalse();
        lang.PluralFamily.Should().Be(PluralRuleFamily.Romance);
    }

    [Fact]
    public void German_HasCorrectProperties()
    {
        var lang = LanguageCode.German;

        lang.Code.Should().Be("de");
        lang.Name.Should().Be("German");
        lang.NativeName.Should().Be("Deutsch");
        lang.IsRightToLeft.Should().BeFalse();
        lang.PluralFamily.Should().Be(PluralRuleFamily.Germanic);
    }

    [Fact]
    public void Arabic_IsRightToLeft()
    {
        var lang = LanguageCode.Arabic;

        lang.Code.Should().Be("ar");
        lang.IsRightToLeft.Should().BeTrue();
        lang.PluralFamily.Should().Be(PluralRuleFamily.Arabic);
    }

    [Fact]
    public void Hebrew_IsRightToLeft()
    {
        var lang = LanguageCode.Hebrew;

        lang.Code.Should().Be("he");
        lang.IsRightToLeft.Should().BeTrue();
        lang.PluralFamily.Should().Be(PluralRuleFamily.Semitic);
    }

    [Fact]
    public void Japanese_HasAsianPluralFamily()
    {
        var lang = LanguageCode.Japanese;

        lang.Code.Should().Be("ja");
        lang.PluralFamily.Should().Be(PluralRuleFamily.Asian);
    }

    [Fact]
    public void Russian_HasSlavicPluralFamily()
    {
        var lang = LanguageCode.Russian;

        lang.Code.Should().Be("ru");
        lang.PluralFamily.Should().Be(PluralRuleFamily.Slavic);
    }

    [Fact]
    public void Polish_HasPolishPluralFamily()
    {
        var lang = LanguageCode.Polish;

        lang.Code.Should().Be("pl");
        lang.PluralFamily.Should().Be(PluralRuleFamily.Polish);
    }

    #endregion

    #region Factory Methods

    [Theory]
    [InlineData("en", "English")]
    [InlineData("EN", "English")]
    [InlineData("it", "Italian")]
    [InlineData("IT", "Italian")]
    [InlineData("de", "German")]
    [InlineData("fr", "French")]
    [InlineData("es", "Spanish")]
    public void FromCode_ValidCode_ReturnsLanguage(string code, string expectedName)
    {
        var lang = LanguageCode.FromCode(code);

        lang.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("invalid")]
    [InlineData("123")]
    [InlineData("")]
    public void FromCode_InvalidCode_ThrowsArgumentException(string code)
    {
        var act = () => LanguageCode.FromCode(code);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"'{code}' is not a valid ISO 639-1 language code.*");
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("it", true)]
    [InlineData("xx", false)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryFromCode_ReturnsExpectedResult(string? code, bool expectedResult)
    {
        var result = LanguageCode.TryFromCode(code, out var language);

        result.Should().Be(expectedResult);
        if (expectedResult)
            language.Code.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("it", true)]
    [InlineData("xx", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ReturnsExpectedResult(string? code, bool expected)
    {
        LanguageCode.IsValid(code).Should().Be(expected);
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_SameLanguage_ReturnsTrue()
    {
        var lang1 = LanguageCode.English;
        var lang2 = LanguageCode.FromCode("en");

        lang1.Should().Be(lang2);
        (lang1 == lang2).Should().BeTrue();
        (lang1 != lang2).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentLanguage_ReturnsFalse()
    {
        var lang1 = LanguageCode.English;
        var lang2 = LanguageCode.Italian;

        lang1.Should().NotBe(lang2);
        (lang1 == lang2).Should().BeFalse();
        (lang1 != lang2).Should().BeTrue();
    }

    [Fact]
    public void Equals_CaseInsensitive()
    {
        var lang1 = LanguageCode.FromCode("en");
        var lang2 = LanguageCode.FromCode("EN");

        lang1.Should().Be(lang2);
    }

    [Fact]
    public void GetHashCode_SameLanguage_ReturnsSameValue()
    {
        var lang1 = LanguageCode.English;
        var lang2 = LanguageCode.FromCode("en");

        lang1.GetHashCode().Should().Be(lang2.GetHashCode());
    }

    #endregion

    #region Conversion

    [Fact]
    public void ImplicitConversionToString_ReturnsCode()
    {
        string code = LanguageCode.English;

        code.Should().Be("en");
    }

    [Fact]
    public void ImplicitConversionFromString_ValidCode_ReturnsLanguage()
    {
        LanguageCode lang = "it";

        lang.Should().Be(LanguageCode.Italian);
    }

    [Fact]
    public void ImplicitConversionFromString_InvalidCode_ThrowsArgumentException()
    {
        var act = () => { LanguageCode lang = "invalid"; };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_ReturnsCode()
    {
        LanguageCode.English.ToString().Should().Be("en");
        LanguageCode.Italian.ToString().Should().Be("it");
    }

    #endregion
}
