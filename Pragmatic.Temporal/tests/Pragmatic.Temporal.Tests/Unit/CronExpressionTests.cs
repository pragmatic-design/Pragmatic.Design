using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

public class CronExpressionTests
{
    #region Parsing

    [Fact]
    public void Parse_ValidExpression_ReturnsExpression()
    {
        var cron = CronExpression.Parse("0 0 * * *");

        cron.Expression.Should().Be("0 0 * * *");
        cron.HasSeconds.Should().BeFalse();
    }

    [Fact]
    public void Parse_SixPartExpression_HasSeconds()
    {
        var cron = CronExpression.Parse("0 0 0 * * *");

        cron.HasSeconds.Should().BeTrue();
    }

    [Fact]
    public void Parse_InvalidExpression_ThrowsFormatException()
    {
        var act = () => CronExpression.Parse("invalid");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_InvalidExpression_ReturnsFalse()
    {
        var result = CronExpression.TryParse("invalid", out var cron);

        result.Should().BeFalse();
        cron.Should().BeNull();
    }

    #endregion

    #region Semantics

    [Fact]
    public void Parse_Default_UsesUnixSemantics()
    {
        var cron = CronExpression.Parse("0 0 1 * 1");

        cron.Semantics.Should().Be(CronSemantics.Unix);
    }

    [Fact]
    public void Parse_WithQuartzSemantics_UsesQuartzSemantics()
    {
        var cron = CronExpression.Parse("0 0 1 * 1", CronSemantics.Quartz);

        cron.Semantics.Should().Be(CronSemantics.Quartz);
    }

    [Theory]
    [InlineData("0 0 1 * *")] // Every 1st of month
    [InlineData("0 0 * * 1")] // Every Monday
    public void UnixSemantics_WildcardField_OnlyChecksOther(string expression)
    {
        var cron = CronExpression.Parse(expression, CronSemantics.Unix);

        // Should have matches (wildcards work correctly)
        var next = cron.GetNextOccurrence(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        next.Should().NotBeNull();
    }

    [Fact]
    public void UnixSemantics_BothFieldsSpecified_UsesOrLogic()
    {
        // "Every 15th OR every Monday at midnight"
        var cron = CronExpression.Parse("0 0 15 * 1", CronSemantics.Unix);

        // Jan 1, 2024 is a Monday
        var monday = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        cron.Matches(monday).Should().BeTrue("it's a Monday");

        // Jan 15, 2024 is a Monday too, but let's check Jan 15, 2025 which is Wednesday
        var fifteenth = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        cron.Matches(fifteenth).Should().BeTrue("it's the 15th");

        // Jan 2, 2025 is Thursday, not 15th - should NOT match
        var neither = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero);
        cron.Matches(neither).Should().BeFalse("it's neither Monday nor 15th");
    }

    [Fact]
    public void QuartzSemantics_BothFieldsSpecified_UsesAndLogic()
    {
        // "Every 15th that is also a Monday at midnight"
        var cron = CronExpression.Parse("0 0 15 * 1", CronSemantics.Quartz);

        // Jan 15, 2024 is Monday - matches both
        var mondayThe15Th = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        cron.Matches(mondayThe15Th).Should().BeTrue("it's both Monday AND 15th");

        // Jan 1, 2024 is Monday but not 15th - should NOT match
        var mondayNotFifteenth = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        cron.Matches(mondayNotFifteenth).Should().BeFalse("it's Monday but not 15th");

        // Jan 15, 2025 is Wednesday (15th but not Monday) - should NOT match
        var fifteenthNotMonday = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);
        cron.Matches(fifteenthNotMonday).Should().BeFalse("it's 15th but not Monday");
    }

    #endregion

    #region GetNextOccurrence

    [Fact]
    public void GetNextOccurrence_EveryMinute_ReturnsNextMinute()
    {
        var cron = CronExpression.EveryMinute;
        var from = new DateTimeOffset(2024, 1, 1, 12, 30, 0, TimeSpan.Zero);

        var next = cron.GetNextOccurrence(from);

        next.Should().Be(new DateTimeOffset(2024, 1, 1, 12, 31, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetNextOccurrence_Midnight_ReturnsNextMidnight()
    {
        var cron = CronExpression.Midnight;
        var from = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var next = cron.GetNextOccurrence(from);

        next.Should().Be(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetOccurrences_ReturnsMultipleOccurrences()
    {
        var cron = CronExpression.Parse("0 0 * * *"); // Daily at midnight
        var from = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var until = new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero);

        var occurrences = cron.GetOccurrences(from, until).ToList();

        occurrences.Should().HaveCount(4); // Jan 2, 3, 4, 5
    }

    [Fact]
    public void GetOccurrences_AcrossAmbiguousDstFallBack_DoesNotStallAndIsStrictlyIncreasing()
    {
        // US Eastern fall-back 2024-11-03: 01:00–01:59 local occurs twice (ambiguous).
        var zone = FindEasternZone();
        var cron = CronExpression.Parse("30 1 * * *"); // 01:30 daily (ambiguous on fall-back day)

        // 'from' is the first (DST) 01:30; the standard-offset 01:30 is later in UTC.
        var from = new DateTimeOffset(2024, 11, 3, 1, 30, 0, TimeSpan.FromHours(-4));

        var occurrences = cron.GetOccurrences(from, zone: zone, maxOccurrences: 2).ToList();

        occurrences.Should().HaveCount(2);
        occurrences[0].Should().BeAfter(from);            // strictly after, no past/duplicate
        occurrences[1].Should().BeAfter(occurrences[0]);  // strictly increasing, no stall
    }

    private static TimeZoneInfo FindEasternZone()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // try next id
            }
        }

        throw new InvalidOperationException("Eastern time zone not available on this platform.");
    }

    #endregion

    #region Common Expressions

    [Fact]
    public void EveryMinute_MatchesEveryMinute()
    {
        var cron = CronExpression.EveryMinute;

        cron.Matches(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 1, 0, 1, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 1, 23, 59, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public void Weekdays_MatchesMondayToFriday()
    {
        var cron = CronExpression.Weekdays;

        // Monday Jan 1, 2024
        cron.Matches(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        // Friday Jan 5, 2024
        cron.Matches(new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        // Saturday Jan 6, 2024
        cron.Matches(new DateTimeOffset(2024, 1, 6, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        // Sunday Jan 7, 2024
        cron.Matches(new DateTimeOffset(2024, 1, 7, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    #endregion

    #region Special Characters

    [Fact]
    public void Parse_WithRanges_MatchesRange()
    {
        var cron = CronExpression.Parse("0 9-17 * * *"); // 9 AM to 5 PM

        cron.Matches(new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 1, 17, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        cron.Matches(new DateTimeOffset(2024, 1, 1, 18, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void Parse_WithStep_MatchesStepValues()
    {
        var cron = CronExpression.Parse("*/15 * * * *"); // Every 15 minutes

        cron.Matches(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 1, 0, 15, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 1, 0, 30, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 1, 0, 45, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 1, 0, 10, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void Parse_WithList_MatchesListValues()
    {
        var cron = CronExpression.Parse("0 0 1,15 * *"); // 1st and 15th

        cron.Matches(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        cron.Matches(new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void Parse_LastDayOfMonth_MatchesLastDay()
    {
        var cron = CronExpression.Parse("0 0 L * *"); // Last day of month

        // Jan 31
        cron.Matches(new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        // Feb 29 (2024 is leap year)
        cron.Matches(new DateTimeOffset(2024, 2, 29, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        // Not last day
        cron.Matches(new DateTimeOffset(2024, 1, 30, 0, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_SameExpression_ReturnsTrue()
    {
        var cron1 = CronExpression.Parse("0 0 * * *");
        var cron2 = CronExpression.Parse("0 0 * * *");

        cron1.Should().Be(cron2);
    }

    [Fact]
    public void Equals_DifferentExpression_ReturnsFalse()
    {
        var cron1 = CronExpression.Parse("0 0 * * *");
        var cron2 = CronExpression.Parse("0 1 * * *");

        cron1.Should().NotBe(cron2);
    }

    #endregion
}