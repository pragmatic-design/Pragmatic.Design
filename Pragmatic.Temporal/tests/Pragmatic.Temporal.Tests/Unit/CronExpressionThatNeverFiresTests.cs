using Pragmatic.Temporal.Types;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     <see cref="CronExpression.GetNextOccurrence" /> answers null for an expression with no occurrence (#624), and still finds the rare ones.
/// </summary>
/// <remarks>
///     The search was bounded by a count of steps, and a day that never comes costs about forty steps a year, so the search ran to year 9999
///     and threw there. It is bounded by the Gregorian cycle instead: dates and days of the week repeat every 400 years, so an expression that
///     does not fire within 400 years of the start never fires.
/// </remarks>
public class CronExpressionThatNeverFiresTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheThirtyFirstOfFebruary_AnswersNull()
    {
        CronExpression.Parse("0 6 31 2 *").GetNextOccurrence(Start).Should().BeNull();
    }

    [Fact]
    public void TheThirtyFirstOfFebruary_ListsNoOccurrence()
    {
        CronExpression.Parse("0 6 31 2 *").GetOccurrences(Start).Count().Should().Be(0);
    }

    [Fact]
    public void TheTwentyNinthOfFebruary_AnswersTheNextOne()
    {
        var next = CronExpression.Parse("0 0 29 2 *").GetNextOccurrence(Start);

        next.Should().Be(new DateTimeOffset(2028, 2, 29, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>2096 is the last leap year before 2104: 2100 is not one, so the gap is eight years.</summary>
    [Fact]
    public void TheTwentyNinthOfFebruary_AcrossACenturyThatIsNotLeap()
    {
        var next = CronExpression.Parse("0 0 29 2 *").GetNextOccurrence(new DateTimeOffset(2096, 3, 1, 0, 0, 0, TimeSpan.Zero));

        next.Should().Be(new DateTimeOffset(2104, 2, 29, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    ///     The control for the bound: with Quartz semantics day of month and day of week must both hold, and a 29 February that is a Monday
    ///     comes every 28 years. A bound of a few years would answer null here.
    /// </summary>
    [Fact]
    public void ARareQuartzDay_IsStillFound()
    {
        var next = CronExpression.Parse("0 0 29 2 1", CronSemantics.Quartz).GetNextOccurrence(Start);

        next.Should().Be(new DateTimeOffset(2044, 2, 29, 0, 0, 0, TimeSpan.Zero));
        next!.Value.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void AStartInTheLastYears_DoesNotThrow()
    {
        var cron = CronExpression.Parse("0 6 31 2 *");

        cron.GetNextOccurrence(new DateTimeOffset(9998, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeNull();
        cron.GetNextOccurrence(new DateTimeOffset(9999, 12, 31, 23, 0, 0, TimeSpan.Zero)).Should().BeNull();
    }

    [Fact]
    public void AnOccurrenceInTheLastYear_IsStillFound()
    {
        var next = CronExpression.Parse("0 0 1 6 *").GetNextOccurrence(new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero));

        next.Should().Be(new DateTimeOffset(9999, 6, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void EveryMinute_AtTheLastMinuteOfTime_AnswersNull()
    {
        CronExpression.Parse("* * * * *").GetNextOccurrence(new DateTimeOffset(9999, 12, 31, 23, 59, 0, TimeSpan.Zero)).Should().BeNull();
    }
}
