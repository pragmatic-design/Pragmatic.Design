using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Tests for the less-common cron special characters supported by <see cref="CronField" />:
///     <c>#</c> (nth weekday of month), <c>W</c> (nearest weekday to a day-of-month),
///     numeric/name day-of-week handling, and month/day name parsing.
/// </summary>
/// <remarks>
///     All evaluations use UTC (the default zone) so they are deterministic regardless of the
///     machine's local timezone. Field order is the standard 5-field form:
///     minute hour day-of-month month day-of-week.
/// </remarks>
public class CronSpecialCharacterTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0)
        => new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    #region Nth Weekday (#)

    [Fact]
    public void Matches_SecondSunday_MatchesOnlyTheSecondSunday()
    {
        // January 2024 Sundays: 7, 14, 21, 28 → second Sunday is the 14th.
        // The # modifier uses the numeric day-of-week form (0 = Sunday).
        var cron = CronExpression.Parse("0 0 * * 0#2");

        cron.Matches(Utc(2024, 1, 14)).Should().BeTrue("Jan 14 is the second Sunday");
        cron.Matches(Utc(2024, 1, 7)).Should().BeFalse("Jan 7 is the first Sunday");
        cron.Matches(Utc(2024, 1, 21)).Should().BeFalse("Jan 21 is the third Sunday");
    }

    [Fact]
    public void Matches_FourthSunday_DoesNotMatchSecondSunday()
    {
        // 0#4 → fourth Sunday of January 2024 is the 28th.
        var cron = CronExpression.Parse("0 0 * * 0#4");

        cron.Matches(Utc(2024, 1, 28)).Should().BeTrue("Jan 28 is the fourth Sunday");
        cron.Matches(Utc(2024, 1, 14)).Should().BeFalse("Jan 14 is the second Sunday");
    }

[Fact]
    public void Parse_InvalidNthModifier_Throws()
    {
        // Nth must be 1..5.
        var act = () => CronExpression.Parse("0 0 * * 0#9");

        act.Should().Throw<FormatException>();
    }

    #endregion

    #region Nearest Weekday (W)

    [Fact]
    public void Matches_NearestWeekday_TargetOnSaturday_MovesToPrecedingFriday()
    {
        // Jun 15, 2024 is a Saturday → nearest weekday is Friday Jun 14.
        var cron = CronExpression.Parse("0 0 15W * *");

        cron.Matches(Utc(2024, 6, 14)).Should().BeTrue("Jun 14 (Fri) is nearest to Sat the 15th");
        cron.Matches(Utc(2024, 6, 15)).Should().BeFalse("the 15th itself is a weekend");
    }

    [Fact]
    public void Matches_NearestWeekday_TargetOnSunday_MovesToFollowingMonday()
    {
        // Sep 15, 2024 is a Sunday → nearest weekday is Monday Sep 16.
        var cron = CronExpression.Parse("0 0 15W * *");

        cron.Matches(Utc(2024, 9, 16)).Should().BeTrue("Sep 16 (Mon) is nearest to Sun the 15th");
        cron.Matches(Utc(2024, 9, 15)).Should().BeFalse("the 15th itself is a weekend");
    }

    [Fact]
    public void Matches_NearestWeekday_TargetOnWeekday_MatchesThatDay()
    {
        // Jan 15, 2024 is a Monday → the 15th itself is the nearest weekday.
        var cron = CronExpression.Parse("0 0 15W * *");

        cron.Matches(Utc(2024, 1, 15)).Should().BeTrue();
    }

    [Fact]
    public void Parse_InvalidWModifier_Throws()
    {
        var act = () => CronExpression.Parse("0 0 99W * *");

        act.Should().Throw<FormatException>();
    }

    #endregion

    #region Day-of-Week 7-as-Sunday

    [Fact]
    public void Matches_DayOfWeekSeven_TreatedAsSunday()
    {
        // Some implementations use 7 for Sunday; both 0 and 7 must match Sunday.
        var cron = CronExpression.Parse("0 0 * * 7");

        // Jan 7, 2024 is a Sunday.
        cron.Matches(Utc(2024, 1, 7)).Should().BeTrue("7 means Sunday");
        cron.Matches(Utc(2024, 1, 8)).Should().BeFalse("Jan 8 is a Monday");
    }

    #endregion

    #region Name Parsing

    [Fact]
    public void Matches_MonthName_ParsedCorrectly()
    {
        // "MAR" → month 3.
        var cron = CronExpression.Parse("0 0 1 MAR *");

        cron.Matches(Utc(2024, 3, 1)).Should().BeTrue();
        cron.Matches(Utc(2024, 4, 1)).Should().BeFalse();
    }

    [Fact]
    public void Matches_DayOfWeekName_ParsedCorrectly()
    {
        // "FRI" → Friday. Jan 5, 2024 is a Friday.
        var cron = CronExpression.Parse("0 0 * * FRI");

        cron.Matches(Utc(2024, 1, 5)).Should().BeTrue();
        cron.Matches(Utc(2024, 1, 6)).Should().BeFalse("Jan 6 is a Saturday");
    }

    #endregion
}
