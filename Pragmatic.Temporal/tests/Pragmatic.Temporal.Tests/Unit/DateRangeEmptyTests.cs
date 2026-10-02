using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Regression tests for finding #7: DateRange.Empty must behave as a truly
///     empty range (no days, no enumeration, no containment, no overlap).
/// </summary>
public class DateRangeEmptyTests
{
    private static readonly DateRange Sample =
        new(new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 10));

    [Fact]
    public void Empty_Days_IsZero()
    {
        DateRange.Empty.Days.Should().Be(0);
    }

    [Fact]
    public void Empty_Duration_IsZero()
    {
        DateRange.Empty.Duration.Should().Be(Duration.Zero);
    }

    [Fact]
    public void Empty_IsSingleDay_IsFalse()
    {
        DateRange.Empty.IsSingleDay.Should().BeFalse();
    }

    [Fact]
    public void Empty_Enumeration_YieldsNothing()
    {
        DateRange.Empty.Should().BeEmpty();
    }

    [Fact]
    public void Empty_ToList_IsEmpty()
    {
        DateRange.Empty.ToList().Should().BeEmpty();
    }

    [Fact]
    public void Empty_WeekdaysAndWeekends_AreEmpty()
    {
        DateRange.Empty.Weekdays().Should().BeEmpty();
        DateRange.Empty.Weekends().Should().BeEmpty();
    }

    [Fact]
    public void Empty_ContainsDate_IsAlwaysFalse()
    {
        DateRange.Empty.Contains(LocalDate.MinValue).Should().BeFalse();
        DateRange.Empty.Contains(new LocalDate(2026, 1, 5)).Should().BeFalse();
    }

    [Fact]
    public void Empty_ContainsRange_IsAlwaysFalse()
    {
        DateRange.Empty.Contains(Sample).Should().BeFalse();
        Sample.Contains(DateRange.Empty).Should().BeFalse();
    }

    [Fact]
    public void Empty_Overlaps_IsAlwaysFalse()
    {
        DateRange.Empty.Overlaps(Sample).Should().BeFalse();
        Sample.Overlaps(DateRange.Empty).Should().BeFalse();
        DateRange.Empty.Overlaps(DateRange.Empty).Should().BeFalse();
    }

    [Fact]
    public void Empty_IsAdjacentTo_IsAlwaysFalse()
    {
        DateRange.Empty.IsAdjacentTo(Sample).Should().BeFalse();
        Sample.IsAdjacentTo(DateRange.Empty).Should().BeFalse();
    }

    [Fact]
    public void Union_WithEmpty_ReturnsOtherRange()
    {
        (DateRange.Empty.Union(Sample) == Sample).Should().BeTrue();
        (Sample.Union(DateRange.Empty) == Sample).Should().BeTrue();
    }

    [Fact]
    public void Intersect_NonOverlapping_StillReturnsEmpty()
    {
        var other = new DateRange(new LocalDate(2026, 3, 1), new LocalDate(2026, 3, 10));

        Sample.Intersect(other).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void DefaultStruct_IsEmpty()
    {
        default(DateRange).IsEmpty.Should().BeTrue();
        default(DateRange).Days.Should().Be(0);
    }
}
