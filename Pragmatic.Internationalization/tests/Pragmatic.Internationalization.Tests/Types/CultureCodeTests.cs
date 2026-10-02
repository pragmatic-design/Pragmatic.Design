using System.Globalization;

using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Types;

/// <summary>
///     Tests for the CultureCode composite type.
/// </summary>
public class CultureCodeTests
{
    #region Static Shortcuts - Language Only

    [Fact]
    public void English_LanguageOnly_HasCorrectProperties()
    {
        var culture = CultureCode.English;

        culture.Code.Should().Be("en");
        culture.Language.Should().Be(LanguageCode.English);
        culture.Country.Should().BeNull();
        culture.IsRightToLeft.Should().BeFalse();
        culture.DefaultCurrency.Should().Be(CurrencyCode.USD);
    }

    [Fact]
    public void Italian_LanguageOnly_HasCorrectProperties()
    {
        var culture = CultureCode.Italian;

        culture.Code.Should().Be("it");
        culture.Language.Should().Be(LanguageCode.Italian);
        culture.Country.Should().BeNull();
        culture.DefaultCurrency.Should().Be(CurrencyCode.EUR);
    }

    [Fact]
    public void Arabic_IsRightToLeft()
    {
        var culture = CultureCode.Arabic;

        culture.IsRightToLeft.Should().BeTrue();
    }

    #endregion

    #region Static Shortcuts - Language + Country

    [Fact]
    public void EnglishUS_HasCorrectProperties()
    {
        var culture = CultureCode.EnglishUS;

        culture.Code.Should().Be("en-US");
        culture.Language.Should().Be(LanguageCode.English);
        culture.Country.Should().Be(CountryCode.US);
        culture.DefaultCurrency.Should().Be(CurrencyCode.USD);
    }

    [Fact]
    public void EnglishUK_HasCorrectProperties()
    {
        var culture = CultureCode.EnglishUK;

        culture.Code.Should().Be("en-GB");
        culture.Language.Should().Be(LanguageCode.English);
        culture.Country.Should().Be(CountryCode.UK);
        culture.DefaultCurrency.Should().Be(CurrencyCode.GBP);
    }

    [Fact]
    public void GermanSwitzerland_HasCorrectProperties()
    {
        var culture = CultureCode.GermanSwitzerland;

        culture.Code.Should().Be("de-CH");
        culture.Language.Should().Be(LanguageCode.German);
        culture.Country.Should().Be(CountryCode.Switzerland);
        culture.DefaultCurrency.Should().Be(CurrencyCode.CHF);
    }

    [Fact]
    public void FrenchCanada_HasCorrectProperties()
    {
        var culture = CultureCode.FrenchCanada;

        culture.Code.Should().Be("fr-CA");
        culture.Language.Should().Be(LanguageCode.French);
        culture.Country.Should().Be(CountryCode.Canada);
        culture.DefaultCurrency.Should().Be(CurrencyCode.CAD);
    }

    #endregion

    #region Constructor

    [Fact]
    public void Constructor_LanguageOnly_SetsCorrectProperties()
    {
        var culture = new CultureCode(LanguageCode.Spanish);

        culture.Language.Should().Be(LanguageCode.Spanish);
        culture.Country.Should().BeNull();
        culture.Code.Should().Be("es");
    }

    [Fact]
    public void Constructor_LanguageAndCountry_SetsCorrectProperties()
    {
        var culture = new CultureCode(LanguageCode.Portuguese, CountryCode.Brazil);

        culture.Language.Should().Be(LanguageCode.Portuguese);
        culture.Country.Should().Be(CountryCode.Brazil);
        culture.Code.Should().Be("pt-BR");
    }

    #endregion

    #region Factory Methods

    [Theory]
    [InlineData("en", "en", true)]
    [InlineData("it", "it", true)]
    [InlineData("de", "de", true)]
    [InlineData("en-US", "en-US", true)]
    [InlineData("it-IT", "it-IT", true)]
    [InlineData("de-CH", "de-CH", true)]
    public void FromString_ValidCode_ReturnsCulture(string input, string expectedCode, bool _)
    {
        var culture = CultureCode.FromString(input);

        culture.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("en-XX")]
    [InlineData("xx-US")]
    [InlineData("invalid")]
    [InlineData("")]
    public void FromString_InvalidCode_ThrowsArgumentException(string code)
    {
        var act = () => CultureCode.FromString(code);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"'{code}' is not a valid culture code.*");
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("en-US", true)]
    [InlineData("it-IT", true)]
    [InlineData("xx", false)]
    [InlineData("en-XX", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryFromString_ReturnsExpectedResult(string? code, bool expectedResult)
    {
        var result = CultureCode.TryFromString(code, out var culture);

        result.Should().Be(expectedResult);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("en-US", true)]
    [InlineData("xx", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ReturnsExpectedResult(string? code, bool expected)
    {
        CultureCode.IsValid(code).Should().Be(expected);
    }

    #endregion

    #region FromCultureInfo

    [Fact]
    public void FromCultureInfo_EnUS_ReturnsCulture()
    {
        var cultureInfo = CultureInfo.GetCultureInfo("en-US");

        var culture = CultureCode.FromCultureInfo(cultureInfo);

        culture.Code.Should().Be("en-US");
        culture.Language.Should().Be(LanguageCode.English);
        culture.Country.Should().Be(CountryCode.US);
    }

    [Fact]
    public void FromCultureInfo_Italian_ReturnsCulture()
    {
        var cultureInfo = CultureInfo.GetCultureInfo("it");

        var culture = CultureCode.FromCultureInfo(cultureInfo);

        culture.Code.Should().Be("it");
        culture.Language.Should().Be(LanguageCode.Italian);
    }

    [Fact]
    public void FromCultureInfo_Null_ThrowsArgumentNullException()
    {
        var act = () => CultureCode.FromCultureInfo(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region FromAcceptLanguage

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("it", "it")]
    [InlineData("en-US, en;q=0.9, it;q=0.8", "en-US")]
    [InlineData("it, en;q=0.9", "it")]
    [InlineData("de-CH, de;q=0.9, en;q=0.8", "de-CH")]
    public void FromAcceptLanguage_ValidHeader_ReturnsBestMatch(string header, string expectedCode)
    {
        var culture = CultureCode.FromAcceptLanguage(header);

        culture.Should().NotBeNull();
        culture!.Value.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromAcceptLanguage_EmptyOrNull_ReturnsNull(string? header)
    {
        var culture = CultureCode.FromAcceptLanguage(header);

        culture.Should().BeNull();
    }

    [Fact]
    public void FromAcceptLanguage_NoValidLanguage_ReturnsNull()
    {
        var culture = CultureCode.FromAcceptLanguage("xx, yy;q=0.9");

        culture.Should().BeNull();
    }

    #endregion

    #region ToCultureInfo

    [Fact]
    public void ToCultureInfo_LanguageOnly_ReturnsCultureInfo()
    {
        var culture = CultureCode.Italian;

        var cultureInfo = culture.ToCultureInfo();

        cultureInfo.Name.Should().Be("it");
    }

    [Fact]
    public void ToCultureInfo_LanguageAndCountry_ReturnsCultureInfo()
    {
        var culture = CultureCode.EnglishUS;

        var cultureInfo = culture.ToCultureInfo();

        cultureInfo.Name.Should().Be("en-US");
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_SameCulture_ReturnsTrue()
    {
        var culture1 = CultureCode.EnglishUS;
        var culture2 = CultureCode.FromString("en-US");

        culture1.Should().Be(culture2);
        (culture1 == culture2).Should().BeTrue();
        (culture1 != culture2).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentCulture_ReturnsFalse()
    {
        var culture1 = CultureCode.EnglishUS;
        var culture2 = CultureCode.EnglishUK;

        culture1.Should().NotBe(culture2);
        (culture1 == culture2).Should().BeFalse();
        (culture1 != culture2).Should().BeTrue();
    }

    [Fact]
    public void Equals_LanguageOnlyVsLanguageWithCountry_ReturnsFalse()
    {
        var culture1 = CultureCode.English;
        var culture2 = CultureCode.EnglishUS;

        culture1.Should().NotBe(culture2);
    }

    [Fact]
    public void GetHashCode_SameCulture_ReturnsSameValue()
    {
        var culture1 = CultureCode.EnglishUS;
        var culture2 = CultureCode.FromString("en-US");

        culture1.GetHashCode().Should().Be(culture2.GetHashCode());
    }

    #endregion

    #region Conversion

    [Fact]
    public void ImplicitConversionToString_ReturnsCode()
    {
        string code = CultureCode.EnglishUS;

        code.Should().Be("en-US");
    }

    [Fact]
    public void ImplicitConversionFromString_ValidCode_ReturnsCulture()
    {
        CultureCode culture = "it-IT";

        culture.Code.Should().Be("it-IT");
    }

    [Fact]
    public void ImplicitConversionToCultureInfo_ReturnsCultureInfo()
    {
        CultureInfo cultureInfo = CultureCode.EnglishUS;

        cultureInfo.Name.Should().Be("en-US");
    }

    [Fact]
    public void ToString_ReturnsCode()
    {
        CultureCode.EnglishUS.ToString().Should().Be("en-US");
        CultureCode.Italian.ToString().Should().Be("it");
    }

    #endregion

    #region DefaultCurrency

    [Fact]
    public void DefaultCurrency_WithCountry_UsesCountryCurrency()
    {
        CultureCode.EnglishUS.DefaultCurrency.Should().Be(CurrencyCode.USD);
        CultureCode.EnglishUK.DefaultCurrency.Should().Be(CurrencyCode.GBP);
        CultureCode.GermanSwitzerland.DefaultCurrency.Should().Be(CurrencyCode.CHF);
    }

    [Fact]
    public void DefaultCurrency_LanguageOnly_UsesFallback()
    {
        CultureCode.English.DefaultCurrency.Should().Be(CurrencyCode.USD);
        CultureCode.Italian.DefaultCurrency.Should().Be(CurrencyCode.EUR);
        CultureCode.Japanese.DefaultCurrency.Should().Be(CurrencyCode.JPY);
    }

    #endregion

    #region PluralFamily

    [Fact]
    public void PluralFamily_DelegatesFromLanguage()
    {
        CultureCode.English.PluralFamily.Should().Be(PluralRuleFamily.Germanic);
        CultureCode.Italian.PluralFamily.Should().Be(PluralRuleFamily.Romance);
        CultureCode.EnglishUS.PluralFamily.Should().Be(PluralRuleFamily.Germanic);
    }

    #endregion
}
