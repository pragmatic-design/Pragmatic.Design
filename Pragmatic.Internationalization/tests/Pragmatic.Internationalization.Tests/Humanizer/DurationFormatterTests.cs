using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Humanizer;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Humanizer;

public class DurationFormatterTests
{
    // Short format tests (English)
    [Theory]
    [InlineData(0, 0, 0, 30, "30s")]
    [InlineData(0, 0, 5, 0, "5m")]
    [InlineData(0, 0, 5, 30, "5m 30s")]
    [InlineData(0, 2, 0, 0, "2h")]
    [InlineData(0, 2, 30, 0, "2h 30m")]
    [InlineData(0, 2, 30, 45, "2h 30m")] // maxParts = 2
    [InlineData(1, 0, 0, 0, "1d")]
    [InlineData(1, 5, 0, 0, "1d 5h")]
    [InlineData(1, 5, 30, 0, "1d 5h")] // maxParts = 2
    public void Format_Short_English(int days, int hours, int minutes, int seconds, string expected)
    {
        // Arrange
        var duration = new TimeSpan(days, hours, minutes, seconds);
        var formatter = new DurationFormatter("en");

        // Act
        var result = formatter.Format(duration);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Format_Short_Zero_ReturnsZeroSeconds()
    {
        // Arrange
        var formatter = new DurationFormatter("en");

        // Act
        var result = formatter.Format(TimeSpan.Zero);

        // Assert
        result.Should().Be("0s");
    }

    [Fact]
    public void Format_Short_Negative_PreservesSign()
    {
        // Arrange
        var formatter = new DurationFormatter("en");

        // Act
        var result = formatter.Format(TimeSpan.FromMinutes(-90));

        // Assert
        result.Should().Be("-1h 30m");
    }

    [Fact]
    public void Format_Short_Milliseconds_WhenSmallDuration()
    {
        // Arrange
        var formatter = new DurationFormatter("en", DurationFormat.Short, 1);

        // Act
        var result = formatter.Format(TimeSpan.FromMilliseconds(500));

        // Assert
        result.Should().Be("500ms");
    }

    // Long format tests (English)
    [Theory]
    [InlineData(0, 0, 1, 0, "1 minute")]
    [InlineData(0, 0, 5, 0, "5 minutes")]
    [InlineData(0, 1, 0, 0, "1 hour")]
    [InlineData(0, 2, 0, 0, "2 hours")]
    [InlineData(0, 1, 30, 0, "1 hour 30 minutes")]
    [InlineData(0, 2, 1, 0, "2 hours 1 minute")]
    [InlineData(1, 0, 0, 0, "1 day")]
    [InlineData(2, 0, 0, 0, "2 days")]
    [InlineData(1, 1, 0, 0, "1 day 1 hour")]
    public void Format_Long_English(int days, int hours, int minutes, int seconds, string expected)
    {
        // Arrange
        var duration = new TimeSpan(days, hours, minutes, seconds);
        var formatter = new DurationFormatter("en", DurationFormat.Long);

        // Act
        var result = formatter.Format(duration);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Format_Long_Zero_ReturnsZeroSeconds()
    {
        // Arrange
        var formatter = new DurationFormatter("en", DurationFormat.Long);

        // Act
        var result = formatter.Format(TimeSpan.Zero);

        // Assert
        result.Should().Be("0 seconds");
    }

    // Compact format tests
    [Theory]
    [InlineData(0, 0, 5, 30, "5:30")]
    [InlineData(0, 2, 30, 0, "2:30:00")]
    [InlineData(0, 2, 5, 30, "2:05:30")]
    [InlineData(1, 2, 30, 45, "1:02:30:45")]
    public void Format_Compact(int days, int hours, int minutes, int seconds, string expected)
    {
        // Arrange
        var duration = new TimeSpan(days, hours, minutes, seconds);
        var formatter = new DurationFormatter("en", DurationFormat.Compact);

        // Act
        var result = formatter.Format(duration);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Format_Compact_Zero_ReturnsZero()
    {
        // Arrange
        var formatter = new DurationFormatter("en", DurationFormat.Compact);

        // Act
        var result = formatter.Format(TimeSpan.Zero);

        // Assert
        result.Should().Be("0:00");
    }

    [Fact]
    public void Format_Compact_Negative_PreservesSign()
    {
        // Arrange
        var formatter = new DurationFormatter("en", DurationFormat.Compact);

        // Act
        var result = formatter.Format(TimeSpan.FromMinutes(-90));

        // Assert
        result.Should().Be("-1:30:00");
    }

    // Multi-language short format
    [Theory]
    [InlineData("it", "2h 30m")]
    [InlineData("de", "2Std 30Min")]
    [InlineData("fr", "2h 30min")]
    [InlineData("es", "2h 30min")]
    [InlineData("ru", "2ч 30мин")]
    [InlineData("zh", "2小时 30分")]
    [InlineData("ja", "2時間 30分")]
    public void Format_Short_DifferentCultures(string culture, string expected)
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(150);
        var formatter = new DurationFormatter(culture);

        // Act
        var result = formatter.Format(duration);

        // Assert
        result.Should().Be(expected);
    }

    // Multi-language long format
    [Theory]
    [InlineData("it", "2 ore 30 minuti")]
    [InlineData("de", "2 Stunden 30 Minuten")]
    [InlineData("fr", "2 heures 30 minutes")]
    [InlineData("es", "2 horas 30 minutos")]
    public void Format_Long_DifferentCultures(string culture, string expected)
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(150);
        var formatter = new DurationFormatter(culture, DurationFormat.Long);

        // Act
        var result = formatter.Format(duration);

        // Assert
        result.Should().Be(expected);
    }

    // MaxParts tests
    [Theory]
    [InlineData(1, "1d")]
    [InlineData(2, "1d 5h")]
    [InlineData(3, "1d 5h 30m")]
    [InlineData(4, "1d 5h 30m 45s")]
    public void Format_MaxParts_LimitsOutput(int maxParts, string expected)
    {
        // Arrange
        var duration = new TimeSpan(1, 5, 30, 45);
        var formatter = new DurationFormatter("en", DurationFormat.Short, maxParts);

        // Act
        var result = formatter.Format(duration);

        // Assert
        result.Should().Be(expected);
    }

    // ShowZeroParts tests
    [Fact]
    public void Format_ShowZeroParts_IncludesZeros()
    {
        // Arrange
        var duration = new TimeSpan(1, 0, 30, 0);
        var formatter = new DurationFormatter("en", DurationFormat.Short, 3, true);

        // Act
        var result = formatter.Format(duration);

        // Assert
        result.Should().Be("1d 0h 30m");
    }

    // FromSeconds and FromMilliseconds
    [Fact]
    public void Format_FromSeconds_Works()
    {
        // Arrange
        var formatter = new DurationFormatter("en");

        // Act
        var result = formatter.Format(3661.5); // 1 hour, 1 minute, 1.5 seconds

        // Assert
        result.Should().Be("1h 1m");
    }

    [Fact]
    public void FormatMilliseconds_Works()
    {
        // Arrange
        var formatter = new DurationFormatter("en");

        // Act
        var result = formatter.FormatMilliseconds(3661000); // 1 hour, 1 minute, 1 second

        // Assert
        result.Should().Be("1h 1m");
    }

    // Static factory methods
    [Fact]
    public void Current_ReturnsFormatter()
    {
        // Act
        var formatter = DurationFormatter.Current;

        // Assert
        formatter.Should().NotBeNull();
    }

    [Fact]
    public void ForCulture_CreatesFormatterWithSpecifiedCulture()
    {
        // Act
        var formatter = DurationFormatter.ForCulture("de");
        var result = formatter.Format(TimeSpan.FromHours(2));

        // Assert
        result.Should().Be("2Std");
    }

    [Fact]
    public void Short_CreatesShortFormatter()
    {
        // Act
        var formatter = DurationFormatter.Short("en");
        var result = formatter.Format(TimeSpan.FromMinutes(90));

        // Assert
        result.Should().Be("1h 30m");
    }

    [Fact]
    public void Long_CreatesLongFormatter()
    {
        // Act
        var formatter = DurationFormatter.Long("en");
        var result = formatter.Format(TimeSpan.FromMinutes(90));

        // Assert
        result.Should().Be("1 hour 30 minutes");
    }

    [Fact]
    public void Compact_CreatesCompactFormatter()
    {
        // Act
        var formatter = DurationFormatter.Compact("en");
        var result = formatter.Format(TimeSpan.FromMinutes(90));

        // Assert
        result.Should().Be("1:30:00");
    }

    // Unknown culture fallback
    [Fact]
    public void Format_UnknownCulture_FallsBackToEnglish()
    {
        // Arrange
        var formatter = new DurationFormatter("xx-XX");

        // Act
        var result = formatter.Format(TimeSpan.FromMinutes(90));

        // Assert
        result.Should().Be("1h 30m");
    }

    // Days only
    [Fact]
    public void Format_DaysOnly_FormatsCorrectly()
    {
        // Arrange
        var formatter = new DurationFormatter("en", DurationFormat.Short, 1);

        // Act
        var result = formatter.Format(TimeSpan.FromDays(5));

        // Assert
        result.Should().Be("5d");
    }

    // Large durations
    [Fact]
    public void Format_LargeDuration_FormatsCorrectly()
    {
        // Arrange
        var formatter = new DurationFormatter("en");

        // Act
        var result = formatter.Format(TimeSpan.FromDays(365));

        // Assert
        result.Should().Be("365d");
    }
}