using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Pragmatic.Internationalization.Validation;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Validation;

/// <summary>
///     Tests for CultureValidation helpers.
/// </summary>
public class CultureValidationTests
{
    #region TryValidate

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("en-US")]
    [InlineData("de-CH")]
    public void TryValidate_ValidCulture_ReturnsTrue(string input)
    {
        var result = CultureValidation.TryValidate(input, out var culture, out var error);

        result.Should().BeTrue();
        culture.Code.Should().Be(input);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "Culture code is required.")]
    [InlineData("", "Culture code is required.")]
    [InlineData("   ", "Culture code is required.")]
    [InlineData("xx", "'xx' is not a valid culture code.")]
    [InlineData("invalid", "'invalid' is not a valid culture code.")]
    [InlineData("en-XX", "'en-XX' is not a valid culture code.")]
    public void TryValidate_InvalidCulture_ReturnsFalseWithError(string? input, string expectedError)
    {
        var result = CultureValidation.TryValidate(input, out _, out var error);

        result.Should().BeFalse();
        error.Should().Be(expectedError);
    }

    #endregion

    #region TryValidateSupported

    [Fact]
    public void TryValidateSupported_SupportedCulture_ReturnsTrue()
    {
        var resolver = CreateResolver([CultureCode.Italian, CultureCode.English]);

        var result = CultureValidation.TryValidateSupported("it", resolver, out var culture, out var error);

        result.Should().BeTrue();
        culture.Should().Be(CultureCode.Italian);
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateSupported_UnsupportedCulture_ReturnsFalse()
    {
        var resolver = CreateResolver([CultureCode.Italian, CultureCode.English]);

        var result = CultureValidation.TryValidateSupported("de", resolver, out _, out var error);

        result.Should().BeFalse();
        error.Should().Be("Culture 'de' is not supported.");
    }

    [Fact]
    public void TryValidateSupported_InvalidCulture_ReturnsFalse()
    {
        var resolver = CreateResolver([CultureCode.Italian]);

        var result = CultureValidation.TryValidateSupported("invalid", resolver, out _, out var error);

        result.Should().BeFalse();
        error.Should().Contain("not a valid culture code");
    }

    #endregion

    #region TryValidateLanguage

    [Theory]
    [InlineData("en", "English")]
    [InlineData("it", "Italian")]
    [InlineData("de", "German")]
    [InlineData("EN", "English")]
    public void TryValidateLanguage_ValidLanguage_ReturnsTrue(string input, string expectedName)
    {
        var result = CultureValidation.TryValidateLanguage(input, out var language, out var error);

        result.Should().BeTrue();
        language.Name.Should().Be(expectedName);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "Language code is required.")]
    [InlineData("", "Language code is required.")]
    [InlineData("xx", "'xx' is not a valid ISO 639-1 language code.")]
    public void TryValidateLanguage_InvalidLanguage_ReturnsFalseWithError(string? input, string expectedError)
    {
        var result = CultureValidation.TryValidateLanguage(input, out _, out var error);

        result.Should().BeFalse();
        error.Should().Be(expectedError);
    }

    #endregion

    #region TryValidateCountry

    [Theory]
    [InlineData("US", "United States")]
    [InlineData("IT", "Italy")]
    [InlineData("DE", "Germany")]
    [InlineData("us", "United States")]
    public void TryValidateCountry_ValidCountry_ReturnsTrue(string input, string expectedName)
    {
        var result = CultureValidation.TryValidateCountry(input, out var country, out var error);

        result.Should().BeTrue();
        country.Name.Should().Be(expectedName);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "Country code is required.")]
    [InlineData("", "Country code is required.")]
    [InlineData("XX", "'XX' is not a valid ISO 3166-1 country code.")]
    public void TryValidateCountry_InvalidCountry_ReturnsFalseWithError(string? input, string expectedError)
    {
        var result = CultureValidation.TryValidateCountry(input, out _, out var error);

        result.Should().BeFalse();
        error.Should().Be(expectedError);
    }

    #endregion

    #region TryValidateCurrency

    [Theory]
    [InlineData("USD", "US Dollar")]
    [InlineData("EUR", "Euro")]
    [InlineData("GBP", "British Pound")]
    [InlineData("usd", "US Dollar")]
    public void TryValidateCurrency_ValidCurrency_ReturnsTrue(string input, string expectedName)
    {
        var result = CultureValidation.TryValidateCurrency(input, out var currency, out var error);

        result.Should().BeTrue();
        currency.Name.Should().Be(expectedName);
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "Currency code is required.")]
    [InlineData("", "Currency code is required.")]
    [InlineData("XXX", "'XXX' is not a valid ISO 4217 currency code.")]
    public void TryValidateCurrency_InvalidCurrency_ReturnsFalseWithError(string? input, string expectedError)
    {
        var result = CultureValidation.TryValidateCurrency(input, out _, out var error);

        result.Should().BeFalse();
        error.Should().Be(expectedError);
    }

    #endregion

    #region GetBestMatch

    [Fact]
    public void GetBestMatch_FirstCandidateSupported_ReturnsFirst()
    {
        var resolver = CreateResolver([CultureCode.Italian, CultureCode.English, CultureCode.German]);

        var result = CultureValidation.GetBestMatch(["it", "en", "de"], resolver);

        result.Should().Be(CultureCode.Italian);
    }

    [Fact]
    public void GetBestMatch_FirstNotSupportedSecondIs_ReturnsSecond()
    {
        var resolver = CreateResolver([CultureCode.English, CultureCode.German]);

        var result = CultureValidation.GetBestMatch(["it", "en", "de"], resolver);

        result.Should().Be(CultureCode.English);
    }

    [Fact]
    public void GetBestMatch_NoneSupported_ReturnsNull()
    {
        var resolver = CreateResolver([CultureCode.Japanese]);

        var result = CultureValidation.GetBestMatch(["it", "en", "de"], resolver);

        result.Should().BeNull();
    }

    [Fact]
    public void GetBestMatch_InvalidCandidates_SkipsAndContinues()
    {
        var resolver = CreateResolver([CultureCode.German]);

        var result = CultureValidation.GetBestMatch(["invalid", "xx", "de"], resolver);

        result.Should().Be(CultureCode.German);
    }

    [Fact]
    public void GetBestMatch_LanguageMatchFallback_ReturnsLanguageMatch()
    {
        // Only "it" (language-only) is supported, but request "it-IT"
        var resolver = CreateResolver([CultureCode.Italian]);

        var result = CultureValidation.GetBestMatch(["it-IT"], resolver);

        result.Should().Be(CultureCode.Italian);
    }

    #endregion

    #region GetBestMatchFromAcceptLanguage

    [Theory]
    [InlineData("it", "it")]
    [InlineData("en-US", "en-US")]
    [InlineData("it, en;q=0.9", "it")]
    [InlineData("en-US, en;q=0.9, it;q=0.8", "en-US")]
    [InlineData("de-CH, de;q=0.9, en;q=0.8", "de")]
    public void GetBestMatchFromAcceptLanguage_ValidHeader_ReturnsBestMatch(string header, string expectedCode)
    {
        var resolver = CreateResolver([
            CultureCode.Italian,
            CultureCode.English,
            CultureCode.EnglishUS,
            CultureCode.German
        ]);

        var result = CultureValidation.GetBestMatchFromAcceptLanguage(header, resolver);

        result.Should().NotBeNull();
        result!.Value.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetBestMatchFromAcceptLanguage_EmptyHeader_ReturnsNull(string? header)
    {
        var resolver = CreateResolver([CultureCode.Italian]);

        var result = CultureValidation.GetBestMatchFromAcceptLanguage(header, resolver);

        result.Should().BeNull();
    }

    [Fact]
    public void GetBestMatchFromAcceptLanguage_NoSupportedLanguage_ReturnsNull()
    {
        var resolver = CreateResolver([CultureCode.Japanese]);

        var result = CultureValidation.GetBestMatchFromAcceptLanguage("it, en;q=0.9", resolver);

        result.Should().BeNull();
    }

    #endregion

    #region Helpers

    private static I18NConfigResolver CreateResolver(IReadOnlyList<CultureCode> supported)
    {
        return new I18NConfigResolver([
            new TestConfigProvider
            {
                Priority = 0,
                Config = new I18NConfig
                {
                    DefaultUICulture = supported[0],
                    SupportedCultures = supported
                }
            }
        ]);
    }

    private sealed class TestConfigProvider : II18NConfigProvider
    {
        public int Priority { get; init; }
        public I18NConfig? Config { get; init; }
        public I18NConfig? GetConfiguration() => Config;
    }

    #endregion
}
