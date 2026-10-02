using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Types;

/// <summary>
///     Tests for the CountryCode type (generated from ISO 3166-1).
/// </summary>
public class CountryCodeTests
{
    #region Static Properties

    [Fact]
    public void US_HasCorrectProperties()
    {
        var country = CountryCode.US;

        country.Code.Should().Be("US");
        country.Name.Should().Be("United States");
        country.DefaultLanguage.Should().Be(LanguageCode.English);
        country.DefaultCurrency.Should().Be(CurrencyCode.USD);
        country.NumberDecimalSeparator.Should().Be('.');
        country.NumberGroupSeparator.Should().Be(',');
        country.DateFormat.Should().Be("MM/dd/yyyy");
    }

    [Fact]
    public void Italy_HasCorrectProperties()
    {
        var country = CountryCode.Italy;

        country.Code.Should().Be("IT");
        country.Name.Should().Be("Italy");
        country.NativeName.Should().Be("Italia");
        country.DefaultLanguage.Should().Be(LanguageCode.Italian);
        country.DefaultCurrency.Should().Be(CurrencyCode.EUR);
        country.NumberDecimalSeparator.Should().Be(',');
        country.NumberGroupSeparator.Should().Be('.');
        country.DateFormat.Should().Be("dd/MM/yyyy");
    }

    [Fact]
    public void Germany_HasCorrectProperties()
    {
        var country = CountryCode.Germany;

        country.Code.Should().Be("DE");
        country.Name.Should().Be("Germany");
        country.NativeName.Should().Be("Deutschland");
        country.DefaultLanguage.Should().Be(LanguageCode.German);
        country.DefaultCurrency.Should().Be(CurrencyCode.EUR);
        country.NumberDecimalSeparator.Should().Be(',');
        country.NumberGroupSeparator.Should().Be('.');
    }

    [Fact]
    public void UK_UsesGBCode()
    {
        // CountryCode.UK is a convenience alias for GB
        var country = CountryCode.UK;

        country.Code.Should().Be("GB");
        country.Name.Should().Be("United Kingdom");
        country.DefaultCurrency.Should().Be(CurrencyCode.GBP);
    }

    [Fact]
    public void Switzerland_HasCHFCurrency()
    {
        var country = CountryCode.Switzerland;

        country.Code.Should().Be("CH");
        country.DefaultCurrency.Should().Be(CurrencyCode.CHF);
    }

    [Fact]
    public void Japan_HasCorrectProperties()
    {
        var country = CountryCode.Japan;

        country.Code.Should().Be("JP");
        country.DefaultLanguage.Should().Be(LanguageCode.Japanese);
        country.DefaultCurrency.Should().Be(CurrencyCode.JPY);
    }

    #endregion

    #region Factory Methods

    [Theory]
    [InlineData("US", "United States")]
    [InlineData("us", "United States")]
    [InlineData("IT", "Italy")]
    [InlineData("it", "Italy")]
    [InlineData("DE", "Germany")]
    [InlineData("de", "Germany")]
    public void FromCode_ValidCode_ReturnsCountry(string code, string expectedName)
    {
        var country = CountryCode.FromCode(code);

        country.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData("XX")]
    [InlineData("invalid")]
    [InlineData("123")]
    [InlineData("")]
    public void FromCode_InvalidCode_ThrowsArgumentException(string code)
    {
        var act = () => CountryCode.FromCode(code);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"'{code}' is not a valid ISO 3166-1 country code.*");
    }

    [Theory]
    [InlineData("US", true)]
    [InlineData("IT", true)]
    [InlineData("XX", false)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryFromCode_ReturnsExpectedResult(string? code, bool expectedResult)
    {
        var result = CountryCode.TryFromCode(code, out var country);

        result.Should().Be(expectedResult);
        if (expectedResult)
            country.Code.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("US", true)]
    [InlineData("IT", true)]
    [InlineData("XX", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ReturnsExpectedResult(string? code, bool expected)
    {
        CountryCode.IsValid(code).Should().Be(expected);
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_SameCountry_ReturnsTrue()
    {
        var country1 = CountryCode.US;
        var country2 = CountryCode.FromCode("US");

        country1.Should().Be(country2);
        (country1 == country2).Should().BeTrue();
        (country1 != country2).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentCountry_ReturnsFalse()
    {
        var country1 = CountryCode.US;
        var country2 = CountryCode.Italy;

        country1.Should().NotBe(country2);
        (country1 == country2).Should().BeFalse();
        (country1 != country2).Should().BeTrue();
    }

    [Fact]
    public void Equals_CaseInsensitive()
    {
        var country1 = CountryCode.FromCode("us");
        var country2 = CountryCode.FromCode("US");

        country1.Should().Be(country2);
    }

    [Fact]
    public void GetHashCode_SameCountry_ReturnsSameValue()
    {
        var country1 = CountryCode.US;
        var country2 = CountryCode.FromCode("US");

        country1.GetHashCode().Should().Be(country2.GetHashCode());
    }

    #endregion

    #region Conversion

    [Fact]
    public void ImplicitConversionToString_ReturnsCode()
    {
        string code = CountryCode.US;

        code.Should().Be("US");
    }

    [Fact]
    public void ImplicitConversionFromString_ValidCode_ReturnsCountry()
    {
        CountryCode country = "IT";

        country.Should().Be(CountryCode.Italy);
    }

    [Fact]
    public void ImplicitConversionFromString_InvalidCode_ThrowsArgumentException()
    {
        var act = () => { CountryCode country = "invalid"; };

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_ReturnsCode()
    {
        CountryCode.US.ToString().Should().Be("US");
        CountryCode.Italy.ToString().Should().Be("IT");
    }

    #endregion

    #region Country-Language-Currency Integration

    [Fact]
    public void EuroZoneCountries_HaveEURCurrency()
    {
        CountryCode.Italy.DefaultCurrency.Should().Be(CurrencyCode.EUR);
        CountryCode.Germany.DefaultCurrency.Should().Be(CurrencyCode.EUR);
        CountryCode.France.DefaultCurrency.Should().Be(CurrencyCode.EUR);
        CountryCode.Spain.DefaultCurrency.Should().Be(CurrencyCode.EUR);
    }

    [Fact]
    public void CountryLanguageLink_IsCorrect()
    {
        CountryCode.Italy.DefaultLanguage.Should().Be(LanguageCode.Italian);
        CountryCode.Germany.DefaultLanguage.Should().Be(LanguageCode.German);
        CountryCode.France.DefaultLanguage.Should().Be(LanguageCode.French);
        CountryCode.Japan.DefaultLanguage.Should().Be(LanguageCode.Japanese);
    }

    #endregion
}
