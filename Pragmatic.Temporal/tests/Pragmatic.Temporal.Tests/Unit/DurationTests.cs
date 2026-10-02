using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

public class DurationTests
{
    [Fact]
    public void FromHours_CreatesCorrectDuration()
    {
        var duration = Duration.FromHours(2);

        duration.TotalHours.Should().Be(2);
        duration.TotalMinutes.Should().Be(120);
    }

    [Fact]
    public void FromMinutes_CreatesCorrectDuration()
    {
        var duration = Duration.FromMinutes(90);

        duration.TotalMinutes.Should().Be(90);
        duration.TotalHours.Should().Be(1.5);
    }

    [Fact]
    public void Add_TwoDurations_ReturnsSum()
    {
        var d1 = Duration.FromHours(1);
        var d2 = Duration.FromMinutes(30);

        var result = d1 + d2;

        result.TotalMinutes.Should().Be(90);
    }

    [Fact]
    public void Subtract_TwoDurations_ReturnsDifference()
    {
        var d1 = Duration.FromHours(2);
        var d2 = Duration.FromMinutes(30);

        var result = d1 - d2;

        result.TotalMinutes.Should().Be(90);
    }

    [Fact]
    public void Multiply_ByScalar_ReturnsScaledDuration()
    {
        var duration = Duration.FromHours(2);

        var result = duration * 3;

        result.TotalHours.Should().Be(6);
    }

    [Fact]
    public void ToTimeSpan_ReturnsEquivalentTimeSpan()
    {
        var duration = Duration.FromHours(2.5);

        var result = duration.ToTimeSpan();

        result.Should().Be(TimeSpan.FromHours(2.5));
    }

    [Fact]
    public void Equality_WithSameDuration_ReturnsTrue()
    {
        var d1 = Duration.FromMinutes(90);
        var d2 = Duration.FromHours(1.5);

        (d1 == d2).Should().BeTrue();
    }

    [Fact]
    public void Zero_ReturnsZeroDuration()
    {
        Duration.Zero.TotalMilliseconds.Should().Be(0);
    }

    #region Parse / TryParse

    [Theory]
    [InlineData("PT1H", 1, 0, 0)]
    [InlineData("PT30M", 0, 30, 0)]
    [InlineData("PT45S", 0, 0, 45)]
    [InlineData("PT1H30M", 1, 30, 0)]
    [InlineData("PT2H15M30S", 2, 15, 30)]
    [InlineData("P1D", 24, 0, 0)]
    [InlineData("P1DT12H", 36, 0, 0)]
    [InlineData("PT0S", 0, 0, 0)]
    public void Parse_ValidIso8601_ReturnsDuration(string input, int hours, int minutes, int seconds)
    {
        var duration = Duration.Parse(input);

        var expected = Duration.FromHours(hours) + Duration.FromMinutes(minutes) + Duration.FromSeconds(seconds);
        duration.Should().Be(expected);
    }

    [Fact]
    public void Parse_NegativeDuration_ReturnsNegative()
    {
        var duration = Duration.Parse("-PT1H");

        duration.TotalHours.Should().Be(-1);
        duration.IsNegative.Should().BeTrue();
    }

    [Fact]
    public void Parse_WithDecimalSeconds_ParsesCorrectly()
    {
        // ISO 8601 uses period as decimal separator regardless of culture
        var duration = Duration.Parse("PT1.5S");

        duration.TotalSeconds.Should().Be(1.5);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("1H")]
    [InlineData("T1H")]
    public void Parse_InvalidInput_ThrowsFormatException(string input)
    {
        var act = () => Duration.Parse(input);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_ValidInput_ReturnsTrue()
    {
        var result = Duration.TryParse("PT2H30M", out var duration);

        result.Should().BeTrue();
        duration.TotalHours.Should().Be(2.5);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        var result = Duration.TryParse("invalid", out var duration);

        result.Should().BeFalse();
        duration.Should().Be(Duration.Zero);
    }

    [Fact]
    public void TryParse_NullInput_ReturnsFalse()
    {
        var result = Duration.TryParse(null, out var duration);

        result.Should().BeFalse();
        duration.Should().Be(Duration.Zero);
    }

    [Fact]
    public void TryParse_DecimalWithPeriod_ParsesRegardlessOfCulture()
    {
        // ISO 8601 mandates period as decimal separator
        // This should work even if current culture uses comma
        var result = Duration.TryParse("PT1.5H", out var duration);

        result.Should().BeTrue();
        duration.TotalMinutes.Should().Be(90);
    }

    [Fact]
    public void ToString_RoundTrip_PreservesValue()
    {
        var original = Duration.FromHours(2) + Duration.FromMinutes(30) + Duration.FromSeconds(15);

        var str = original.ToString();
        var parsed = Duration.Parse(str);

        parsed.Should().Be(original);
    }

    #endregion
}