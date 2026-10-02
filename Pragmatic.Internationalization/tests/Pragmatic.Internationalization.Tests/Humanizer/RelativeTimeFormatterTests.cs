using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Internationalization.Humanizer;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Humanizer;

public class RelativeTimeFormatterTests
{
    private readonly RelativeTimeFormatter _formatter;
    private readonly IStringLocalizer _localizer;

    public RelativeTimeFormatterTests()
    {
        _localizer = new StringLocalizerMock();

        // The indexer is configured through Item1 — its arity — and each key gets its own rule.
        var mock = (StringLocalizerMock)_localizer;
        mock.Item1.When(RelativeTimeFormatter.Keys.Now)
            .Returns(TranslationResult.Found(RelativeTimeFormatter.Keys.Now, "just now"));
        mock.Item1.When(RelativeTimeFormatter.Keys.Yesterday)
            .Returns(TranslationResult.Found(RelativeTimeFormatter.Keys.Yesterday, "yesterday"));

        // Plural answers per key, with the count taken from the call's second argument.
        Plural(RelativeTimeFormatter.Keys.Seconds, "seconds");
        Plural(RelativeTimeFormatter.Keys.Minutes, "minutes");
        Plural(RelativeTimeFormatter.Keys.Hours, "hours");
        Plural(RelativeTimeFormatter.Keys.Days, "days");
        Plural(RelativeTimeFormatter.Keys.Weeks, "weeks");
        Plural(RelativeTimeFormatter.Keys.Months, "months");
        Plural(RelativeTimeFormatter.Keys.Years, "years");

        void Plural(string key, string unit) =>
            mock.Plural2.When(key).Returns(call => TranslationResult.Found(key, $"{call.Item2} {unit} ago"));

        _formatter = new RelativeTimeFormatter(_localizer);
    }

    [Fact]
    public void Format_LessThan5Seconds_ReturnsNow()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromSeconds(3));

        // Assert
        result.Value.Should().Be("just now");
    }

    [Fact]
    public void Format_Seconds_ReturnsSecondsAgo()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromSeconds(30));

        // Assert
        result.Value.Should().Be("30 seconds ago");
    }

    [Fact]
    public void Format_Minutes_ReturnsMinutesAgo()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromMinutes(15));

        // Assert
        result.Value.Should().Be("15 minutes ago");
    }

    [Fact]
    public void Format_Hours_ReturnsHoursAgo()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromHours(5));

        // Assert
        result.Value.Should().Be("5 hours ago");
    }

    [Fact]
    public void Format_Yesterday_ReturnsYesterday()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromHours(30));

        // Assert
        result.Value.Should().Be("yesterday");
    }

    [Fact]
    public void Format_Days_ReturnsDaysAgo()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromDays(5));

        // Assert
        result.Value.Should().Be("5 days ago");
    }

    [Fact]
    public void Format_Weeks_ReturnsWeeksAgo()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromDays(14));

        // Assert
        result.Value.Should().Be("2 weeks ago");
    }

    [Fact]
    public void Format_Months_ReturnsMonthsAgo()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromDays(60));

        // Assert
        result.Value.Should().Be("2 months ago");
    }

    [Fact]
    public void Format_Years_ReturnsYearsAgo()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromDays(400));

        // Assert
        result.Value.Should().Be("1 years ago");
    }

    [Fact]
    public void Format_NegativeTimeSpan_TreatsAsAbsoluteValue()
    {
        // Act
        var result = _formatter.Format(TimeSpan.FromHours(-5));

        // Assert
        result.Value.Should().Be("5 hours ago");
    }

    [Fact]
    public void Format_DateTimeOffset_CalculatesFromNow()
    {
        // Arrange
        var pastDate = DateTimeOffset.UtcNow.AddHours(-3);

        // Act
        var result = _formatter.Format(pastDate);

        // Assert
        result.Value.Should().Be("3 hours ago");
    }

    [Fact]
    public void Format_TwoDateTimeOffsets_CalculatesDifference()
    {
        // Arrange
        var from = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2024, 1, 1, 15, 0, 0, TimeSpan.Zero);

        // Act
        var result = _formatter.Format(from, to);

        // Assert
        result.Value.Should().Be("5 hours ago");
    }

    [Fact]
    public void Keys_HaveCorrectValues()
    {
        // Assert
        RelativeTimeFormatter.Keys.Now.Should().Be("TimeAgo.now");
        RelativeTimeFormatter.Keys.Seconds.Should().Be("TimeAgo.seconds");
        RelativeTimeFormatter.Keys.Minutes.Should().Be("TimeAgo.minutes");
        RelativeTimeFormatter.Keys.Hours.Should().Be("TimeAgo.hours");
        RelativeTimeFormatter.Keys.Yesterday.Should().Be("TimeAgo.yesterday");
        RelativeTimeFormatter.Keys.Days.Should().Be("TimeAgo.days");
        RelativeTimeFormatter.Keys.Weeks.Should().Be("TimeAgo.weeks");
        RelativeTimeFormatter.Keys.Months.Should().Be("TimeAgo.months");
        RelativeTimeFormatter.Keys.Years.Should().Be("TimeAgo.years");
    }

    [Fact]
    public void Constructor_NullLocalizer_ThrowsArgumentException()
    {
        // Act & Assert
        var action = () => new RelativeTimeFormatter(null!);
        action.Should().Throw<ArgumentException>();
    }
}