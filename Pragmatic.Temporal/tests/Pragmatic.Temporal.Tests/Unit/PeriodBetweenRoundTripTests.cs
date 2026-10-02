using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Extensions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Round-trip and negative-period semantics for <see cref="Period.Between" /> together with
///     <see cref="LocalDateExtensions.Add(LocalDate, Period)" />.
/// </summary>
/// <remarks>
///     The defining contract: for any start ≤ end where end's day-of-month is reachable,
///     <c>start.Add(Period.Between(start, end)) == end</c>.
///     Note that Period arithmetic is order-dependent (years+months are applied before days),
///     so the round-trip holds for the cases covered here but is not universal across all date pairs.
/// </remarks>
public class PeriodBetweenRoundTripTests
{
    #region Forward Round-Trip

    [Fact]
    public void Between_ThenAdd_SimpleSpan_ReturnsEnd()
    {
        var start = new LocalDate(2024, 1, 15);
        var end = new LocalDate(2025, 3, 20);

        var period = Period.Between(start, end);

        start.Add(period).Should().Be(end);
    }

    [Fact]
    public void Between_ThenAdd_DayBorrow_ReturnsEnd()
    {
        // End day (5) is earlier in the month than start day (28), forcing a day-borrow in Between.
        var start = new LocalDate(2024, 1, 28);
        var end = new LocalDate(2024, 3, 5);

        var period = Period.Between(start, end);

        start.Add(period).Should().Be(end);
    }

    [Fact]
    public void Between_ThenAdd_AcrossLeapDay_ReturnsEnd()
    {
        var start = new LocalDate(2024, 2, 10);
        var end = new LocalDate(2024, 3, 10);

        var period = Period.Between(start, end);

        period.Should().Be(new Period(0, 1, 0));
        start.Add(period).Should().Be(end);
    }

    [Fact]
    public void Between_SameDate_ThenAdd_ReturnsSameDate()
    {
        var date = new LocalDate(2024, 7, 4);

        var period = Period.Between(date, date);

        period.IsZero.Should().BeTrue();
        date.Add(period).Should().Be(date);
    }

    #endregion

    #region Negative Period Detection

    [Fact]
    public void Between_EndBeforeStart_IsNegative()
    {
        var start = new LocalDate(2025, 6, 1);
        var end = new LocalDate(2024, 3, 15);

        var period = Period.Between(start, end);

        period.IsNegative.Should().BeTrue();
    }

    [Fact]
    public void Between_ReversedArguments_NegatesPeriod()
    {
        var start = new LocalDate(2024, 1, 15);
        var end = new LocalDate(2025, 3, 20);

        var forward = Period.Between(start, end);
        var backward = Period.Between(end, start);

        backward.Should().Be(forward.Negate());
    }

    [Fact]
    public void Between_EndBeforeStart_ThenAdd_ReturnsStart()
    {
        // start.Add(Between(start, end)) reconstructs end even when end precedes start.
        var start = new LocalDate(2025, 3, 20);
        var end = new LocalDate(2024, 1, 15);

        var period = Period.Between(start, end);

        period.IsNegative.Should().BeTrue();
        start.Add(period).Should().Be(end);
    }

    [Fact]
    public void Between_OneDayBack_HasNegativeDays()
    {
        var start = new LocalDate(2024, 6, 15);
        var end = new LocalDate(2024, 6, 14);

        var period = Period.Between(start, end);

        period.Should().Be(new Period(0, 0, -1));
        period.IsNegative.Should().BeTrue();
    }

    #endregion
}
