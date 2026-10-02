using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Humanizer;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Humanizer;

public class OrdinalFormatterTests
{
    // English ordinals
    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(5, "5th")]
    [InlineData(10, "10th")]
    [InlineData(11, "11th")] // Special case
    [InlineData(12, "12th")] // Special case
    [InlineData(13, "13th")] // Special case
    [InlineData(14, "14th")]
    [InlineData(20, "20th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    [InlineData(24, "24th")]
    [InlineData(100, "100th")]
    [InlineData(101, "101st")]
    [InlineData(111, "111th")] // Special case
    [InlineData(112, "112th")] // Special case
    [InlineData(113, "113th")] // Special case
    public void Format_English_ReturnsCorrectSuffix(int number, string expected)
    {
        // Arrange
        var formatter = new OrdinalFormatter("en");

        // Act
        var result = formatter.Format(number);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Format_English_NegativeNumbers()
    {
        // Arrange
        var formatter = new OrdinalFormatter("en");

        // Act & Assert
        formatter.Format(-1).Should().Be("-1st");
        formatter.Format(-2).Should().Be("-2nd");
        formatter.Format(-3).Should().Be("-3rd");
        formatter.Format(-11).Should().Be("-11th");
    }

    // Romance languages (Italian, Spanish, Portuguese)
    [Theory]
    [InlineData("it", 1, "1°")]
    [InlineData("it", 2, "2°")]
    [InlineData("it", 10, "10°")]
    [InlineData("es", 1, "1°")]
    [InlineData("es", 5, "5°")]
    [InlineData("pt", 1, "1°")]
    [InlineData("pt", 100, "100°")]
    public void Format_Romance_UsesDegreeSymbol(string culture, int number, string expected)
    {
        // Arrange
        var formatter = new OrdinalFormatter(culture);

        // Act
        var result = formatter.Format(number);

        // Assert
        result.Should().Be(expected);
    }

    // Germanic languages (German, Swedish, Norwegian, Danish, Dutch)
    [Theory]
    [InlineData("de", 1, "1.")]
    [InlineData("de", 5, "5.")]
    [InlineData("sv", 1, "1.")]
    [InlineData("no", 3, "3.")]
    [InlineData("da", 10, "10.")]
    [InlineData("nl", 100, "100.")]
    public void Format_Germanic_UsesPeriod(string culture, int number, string expected)
    {
        // Arrange
        var formatter = new OrdinalFormatter(culture);

        // Act
        var result = formatter.Format(number);

        // Assert
        result.Should().Be(expected);
    }

    // French with gender
    [Theory]
    [InlineData(1, OrdinalGender.Masculine, "1er")]
    [InlineData(1, OrdinalGender.Feminine, "1ère")]
    [InlineData(2, OrdinalGender.Masculine, "2e")]
    [InlineData(2, OrdinalGender.Feminine, "2e")]
    [InlineData(3, OrdinalGender.Masculine, "3e")]
    [InlineData(10, OrdinalGender.Masculine, "10e")]
    public void Format_French_HandlesGender(int number, OrdinalGender gender, string expected)
    {
        // Arrange
        var formatter = new OrdinalFormatter("fr", gender);

        // Act
        var result = formatter.Format(number);

        // Assert
        result.Should().Be(expected);
    }

    // Slavic languages
    [Theory]
    [InlineData("ru", 1, "1-й")]
    [InlineData("ru", 5, "5-й")]
    [InlineData("pl", 1, "1-й")]
    [InlineData("cs", 10, "10-й")]
    public void Format_Slavic_UsesDashSuffix(string culture, int number, string expected)
    {
        // Arrange
        var formatter = new OrdinalFormatter(culture);

        // Act
        var result = formatter.Format(number);

        // Assert
        result.Should().Be(expected);
    }

    // East Asian languages
    [Theory]
    [InlineData("zh", 1, "第1")]
    [InlineData("zh", 5, "第5")]
    [InlineData("ja", 1, "第1")]
    [InlineData("ja", 100, "第100")]
    public void Format_EastAsian_UsesPrefix(string culture, int number, string expected)
    {
        // Arrange
        var formatter = new OrdinalFormatter(culture);

        // Act
        var result = formatter.Format(number);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("ko", 1, "제1")]
    [InlineData("ko", 10, "제10")]
    public void Format_Korean_UsesKoreanPrefix(string culture, int number, string expected)
    {
        // Arrange
        var formatter = new OrdinalFormatter(culture);

        // Act
        var result = formatter.Format(number);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Format_UnknownCulture_FallsBackToEnglish()
    {
        // Arrange
        var formatter = new OrdinalFormatter("xx-XX");

        // Act
        var result = formatter.Format(1);

        // Assert
        result.Should().Be("1st");
    }

    [Fact]
    public void Format_CultureWithRegion_UsesLanguagePart()
    {
        // Arrange - "en-US" should use English rules
        var formatter = new OrdinalFormatter("en-US");

        // Act
        var result = formatter.Format(2);

        // Assert
        result.Should().Be("2nd");
    }

    [Fact]
    public void Format_Long_ConvertsToInt()
    {
        // Arrange
        var formatter = new OrdinalFormatter("en");

        // Act
        var result = formatter.Format(21L);

        // Assert
        result.Should().Be("21st");
    }

    [Fact]
    public void Current_UsesContextCulture()
    {
        // Arrange & Act
        var formatter = OrdinalFormatter.Current;

        // Assert
        formatter.Should().NotBeNull();
    }

    [Fact]
    public void ForCulture_CreatesFormatterForSpecifiedCulture()
    {
        // Act
        var formatter = OrdinalFormatter.ForCulture("de");
        var result = formatter.Format(5);

        // Assert
        result.Should().Be("5.");
    }

    [Fact]
    public void ForCulture_WithGender_CreatesFormatterWithGender()
    {
        // Act
        var formatter = OrdinalFormatter.ForCulture("fr", OrdinalGender.Feminine);
        var result = formatter.Format(1);

        // Assert
        result.Should().Be("1ère");
    }
}