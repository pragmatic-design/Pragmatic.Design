using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Humanizer;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Humanizer;

public class QuantityFormatterTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(10, "10")]
    [InlineData(100, "100")]
    [InlineData(999, "999")]
    public void Format_SmallNumbers_NoSuffix(long value, string expected)
    {
        // Arrange
        var formatter = new QuantityFormatter("en");

        // Act
        var result = formatter.Format(value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1000, "1K")]
    [InlineData(1500, "1.5K")]
    [InlineData(2000, "2K")]
    [InlineData(10000, "10K")]
    [InlineData(15500, "15.5K")]
    [InlineData(999999, "1000K")]
    public void Format_Thousands_EnglishSuffix(long value, string expected)
    {
        // Arrange
        var formatter = new QuantityFormatter("en");

        // Act
        var result = formatter.Format(value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1_000_000, "1M")]
    [InlineData(1_500_000, "1.5M")]
    [InlineData(2_300_000, "2.3M")]
    [InlineData(10_000_000, "10M")]
    [InlineData(999_999_999, "1000M")]
    public void Format_Millions_EnglishSuffix(long value, string expected)
    {
        // Arrange
        var formatter = new QuantityFormatter("en");

        // Act
        var result = formatter.Format(value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1_000_000_000, "1B")]
    [InlineData(1_500_000_000, "1.5B")]
    [InlineData(10_000_000_000, "10B")]
    public void Format_Billions_EnglishSuffix(long value, string expected)
    {
        // Arrange
        var formatter = new QuantityFormatter("en");

        // Act
        var result = formatter.Format(value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1_000_000_000_000, "1T")]
    [InlineData(1_500_000_000_000, "1.5T")]
    public void Format_Trillions_EnglishSuffix(long value, string expected)
    {
        // Arrange
        var formatter = new QuantityFormatter("en");

        // Act
        var result = formatter.Format(value);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Format_NegativeNumber_PreservesSign()
    {
        // Arrange
        var formatter = new QuantityFormatter("en");

        // Act
        var result = formatter.Format(-1500);

        // Assert
        result.Should().Be("-1.5K");
    }

    [Theory]
    [InlineData("it", 1_000_000_000, "1Mld")]
    [InlineData("de", 1_000_000, "1Mio")]
    [InlineData("de", 1_000_000_000, "1Mrd")]
    [InlineData("fr", 1_000_000_000, "1Md")]
    [InlineData("ru", 1_000, "1тыс")]
    [InlineData("ru", 1_000_000, "1млн")]
    [InlineData("zh", 1_000, "1千")]
    [InlineData("ja", 1_000_000, "1百万")]
    public void Format_DifferentCultures_UsesLocalizedSuffix(string culture, long value, string expected)
    {
        // Arrange
        var formatter = new QuantityFormatter(culture);

        // Act
        var result = formatter.Format(value);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Format_ItalianCulture_UsesCommaDecimalSeparator()
    {
        // Arrange
        var formatter = new QuantityFormatter("it-IT");

        // Act
        var result = formatter.Format(1500);

        // Assert
        result.Should().Be("1,5K");
    }

    [Fact]
    public void Format_FrenchCulture_UsesCommaDecimalSeparator()
    {
        // Arrange
        var formatter = new QuantityFormatter("fr-FR");

        // Act
        var result = formatter.Format(1500);

        // Assert
        result.Should().Be("1,5k");
    }

    [Fact]
    public void Format_UnknownCulture_FallsBackToEnglish()
    {
        // Arrange
        var formatter = new QuantityFormatter("xx-XX");

        // Act
        var result = formatter.Format(1_000_000);

        // Assert
        result.Should().Be("1M");
    }

    [Fact]
    public void Format_Decimal_RoundsAndFormats()
    {
        // Arrange
        var formatter = new QuantityFormatter("en");

        // Act
        var result = formatter.Format(1234.56m);

        // Assert
        result.Should().Be("1.2K");
    }

    [Fact]
    public void Format_Double_RoundsAndFormats()
    {
        // Arrange
        var formatter = new QuantityFormatter("en");

        // Act
        var result = formatter.Format(1234.56);

        // Assert
        result.Should().Be("1.2K");
    }

    [Fact]
    public void Format_CustomSuffixes_UsesProvidedValues()
    {
        // Arrange
        var customSuffixes = new QuantitySuffixes("mil", "MM", "bil", "tril");
        var formatter = new QuantityFormatter("en", customSuffixes);

        // Act
        var thousand = formatter.Format(1500);
        var million = formatter.Format(1_500_000);
        var billion = formatter.Format(1_500_000_000);

        // Assert
        thousand.Should().Be("1.5mil");
        million.Should().Be("1.5MM");
        billion.Should().Be("1.5bil");
    }

    [Fact]
    public void Format_CustomDecimals_FormatsCorrectly()
    {
        // Arrange
        var formatter = new QuantityFormatter("en", 2);

        // Act
        var result = formatter.Format(1234);

        // Assert
        result.Should().Be("1.23K");
    }

    [Fact]
    public void Current_UsesContextCulture()
    {
        // Arrange & Act
        var formatter = QuantityFormatter.Current;

        // Assert
        formatter.Should().NotBeNull();
    }

    [Fact]
    public void ForCulture_CreatesFormatterForSpecifiedCulture()
    {
        // Act
        var formatter = QuantityFormatter.ForCulture("de");
        var result = formatter.Format(1_000_000);

        // Assert
        result.Should().Be("1Mio");
    }
}