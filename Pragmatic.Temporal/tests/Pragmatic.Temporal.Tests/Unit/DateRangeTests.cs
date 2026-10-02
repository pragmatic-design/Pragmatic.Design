using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Tests for <see cref="DateRange" />.
/// </summary>
public class DateRangeTests
{
    #region Construction

    [Fact]
    public void Constructor_ValidRange_CreatesInstance()
    {
        var start = new LocalDate(2024, 1, 1);
        var end = new LocalDate(2024, 12, 31);

        var range = new DateRange(start, end);

        range.Start.Should().Be(start);
        range.End.Should().Be(end);
    }

    [Fact]
    public void Constructor_StartAfterEnd_Throws()
    {
        var start = new LocalDate(2024, 12, 31);
        var end = new LocalDate(2024, 1, 1);

        var act = () => new DateRange(start, end);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_SameDay_CreatesInstance()
    {
        var date = new LocalDate(2024, 1, 15);

        var range = new DateRange(date, date);

        range.Start.Should().Be(date);
        range.End.Should().Be(date);
        range.IsSingleDay.Should().BeTrue();
    }

    #endregion

    #region Properties

    [Fact]
    public void Days_ReturnsInclusiveCount()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 10));

        range.Days.Should().Be(10); // Inclusive: 1,2,3,4,5,6,7,8,9,10
    }

    [Fact]
    public void Days_SingleDay_ReturnsOne()
    {
        var range = DateRange.SingleDay(new LocalDate(2024, 1, 15));

        range.Days.Should().Be(1);
    }

    [Fact]
    public void IsSingleDay_SingleDay_ReturnsTrue()
    {
        var range = DateRange.SingleDay(new LocalDate(2024, 1, 15));

        range.IsSingleDay.Should().BeTrue();
    }

    [Fact]
    public void IsSingleDay_MultiDay_ReturnsFalse()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 2));

        range.IsSingleDay.Should().BeFalse();
    }

    [Fact]
    public void Duration_ReturnsCorrectDuration()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 10));

        range.Duration.Should().Be(Duration.FromDays(10));
    }

    #endregion

    #region Factory Methods

    [Fact]
    public void SingleDay_CreatesCorrectRange()
    {
        var date = new LocalDate(2024, 1, 15);

        var range = DateRange.SingleDay(date);

        range.Start.Should().Be(date);
        range.End.Should().Be(date);
        range.Days.Should().Be(1);
    }

    [Fact]
    public void Week_ReturnsWeekContainingDate()
    {
        var wednesday = new LocalDate(2024, 1, 17); // Wednesday

        var range = DateRange.Week(wednesday);

        range.Start.Should().Be(new LocalDate(2024, 1, 15)); // Monday
        range.End.Should().Be(new LocalDate(2024, 1, 21)); // Sunday
        range.Days.Should().Be(7);
    }

    [Fact]
    public void Month_ReturnsWholeMonth()
    {
        var date = new LocalDate(2024, 1, 15);

        var range = DateRange.Month(date);

        range.Start.Should().Be(new LocalDate(2024, 1, 1));
        range.End.Should().Be(new LocalDate(2024, 1, 31));
        range.Days.Should().Be(31);
    }

    [Fact]
    public void Month_February_LeapYear_Returns29Days()
    {
        var range = DateRange.Month(2024, 2);

        range.Days.Should().Be(29);
        range.End.Should().Be(new LocalDate(2024, 2, 29));
    }

    [Fact]
    public void Quarter_Q1_ReturnsJanToMarch()
    {
        var range = DateRange.Quarter(2024, 1);

        range.Start.Should().Be(new LocalDate(2024, 1, 1));
        range.End.Should().Be(new LocalDate(2024, 3, 31));
    }

    [Fact]
    public void Quarter_Q4_ReturnsOctToDecember()
    {
        var range = DateRange.Quarter(2024, 4);

        range.Start.Should().Be(new LocalDate(2024, 10, 1));
        range.End.Should().Be(new LocalDate(2024, 12, 31));
    }

    [Fact]
    public void Year_Returns365Or366Days()
    {
        var range2024 = DateRange.Year(2024);
        var range2023 = DateRange.Year(2023);

        range2024.Days.Should().Be(366); // Leap year
        range2023.Days.Should().Be(365);
    }

    [Fact]
    public void LastDays_ReturnsCorrectRange()
    {
        var endDate = new LocalDate(2024, 1, 15);

        var range = DateRange.LastDays(endDate, 7);

        range.Start.Should().Be(new LocalDate(2024, 1, 9));
        range.End.Should().Be(new LocalDate(2024, 1, 15));
        range.Days.Should().Be(7);
    }

    [Fact]
    public void NextDays_ReturnsCorrectRange()
    {
        var startDate = new LocalDate(2024, 1, 15);

        var range = DateRange.NextDays(startDate, 7);

        range.Start.Should().Be(new LocalDate(2024, 1, 15));
        range.End.Should().Be(new LocalDate(2024, 1, 21));
        range.Days.Should().Be(7);
    }

    [Fact]
    public void Between_OrdersCorrectly()
    {
        var date1 = new LocalDate(2024, 12, 31);
        var date2 = new LocalDate(2024, 1, 1);

        var range = DateRange.Between(date1, date2);

        range.Start.Should().Be(date2);
        range.End.Should().Be(date1);
    }

    #endregion

    #region Operations

    [Fact]
    public void Contains_DateInRange_ReturnsTrue()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 31));

        range.Contains(new LocalDate(2024, 1, 15)).Should().BeTrue();
        range.Contains(new LocalDate(2024, 1, 1)).Should().BeTrue(); // Start inclusive
        range.Contains(new LocalDate(2024, 1, 31)).Should().BeTrue(); // End inclusive
    }

    [Fact]
    public void Contains_DateOutsideRange_ReturnsFalse()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 31));

        range.Contains(new LocalDate(2023, 12, 31)).Should().BeFalse();
        range.Contains(new LocalDate(2024, 2, 1)).Should().BeFalse();
    }

    [Fact]
    public void Contains_Range_ContainedRange_ReturnsTrue()
    {
        var outer = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31));
        var inner = new DateRange(new LocalDate(2024, 3, 1), new LocalDate(2024, 3, 31));

        outer.Contains(inner).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_OverlappingRanges_ReturnsTrue()
    {
        var range1 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 15));
        var range2 = new DateRange(new LocalDate(2024, 1, 10), new LocalDate(2024, 1, 31));

        range1.Overlaps(range2).Should().BeTrue();
        range2.Overlaps(range1).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_AdjacentRanges_ReturnsTrue()
    {
        var range1 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 15));
        var range2 = new DateRange(new LocalDate(2024, 1, 15), new LocalDate(2024, 1, 31));

        range1.Overlaps(range2).Should().BeTrue(); // Share Jan 15
    }

    [Fact]
    public void Overlaps_NonOverlappingRanges_ReturnsFalse()
    {
        var range1 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 14));
        var range2 = new DateRange(new LocalDate(2024, 1, 16), new LocalDate(2024, 1, 31));

        range1.Overlaps(range2).Should().BeFalse();
    }

    [Fact]
    public void IsAdjacentTo_AdjacentRanges_ReturnsTrue()
    {
        var range1 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 14));
        var range2 = new DateRange(new LocalDate(2024, 1, 15), new LocalDate(2024, 1, 31));

        range1.IsAdjacentTo(range2).Should().BeTrue();
        range2.IsAdjacentTo(range1).Should().BeTrue();
    }

    [Fact]
    public void Intersect_OverlappingRanges_ReturnsIntersection()
    {
        var range1 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 20));
        var range2 = new DateRange(new LocalDate(2024, 1, 10), new LocalDate(2024, 1, 31));

        var intersection = range1.Intersect(range2);

        intersection.Start.Should().Be(new LocalDate(2024, 1, 10));
        intersection.End.Should().Be(new LocalDate(2024, 1, 20));
    }

    [Fact]
    public void Intersect_NonOverlapping_ReturnsEmpty()
    {
        var range1 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 10));
        var range2 = new DateRange(new LocalDate(2024, 2, 1), new LocalDate(2024, 2, 28));

        var intersection = range1.Intersect(range2);

        intersection.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Union_ReturnsSmallestEnclosingRange()
    {
        var range1 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 15));
        var range2 = new DateRange(new LocalDate(2024, 2, 1), new LocalDate(2024, 2, 28));

        var union = range1.Union(range2);

        union.Start.Should().Be(new LocalDate(2024, 1, 1));
        union.End.Should().Be(new LocalDate(2024, 2, 28));
    }

    [Fact]
    public void Expand_ExpandsBothEnds()
    {
        var range = new DateRange(new LocalDate(2024, 1, 10), new LocalDate(2024, 1, 20));

        var expanded = range.Expand(5);

        expanded.Start.Should().Be(new LocalDate(2024, 1, 5));
        expanded.End.Should().Be(new LocalDate(2024, 1, 25));
    }

    [Fact]
    public void Shift_MovesEntireRange()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 31));

        var shifted = range.Shift(30);

        shifted.Start.Should().Be(new LocalDate(2024, 1, 31));
        shifted.End.Should().Be(new LocalDate(2024, 3, 1));
    }

    [Fact]
    public void Split_SplitsIntoChunks()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 10));

        var chunks = range.Split(3).ToList();

        chunks.Should().HaveCount(4);
        chunks[0].Days.Should().Be(3); // Jan 1-3
        chunks[1].Days.Should().Be(3); // Jan 4-6
        chunks[2].Days.Should().Be(3); // Jan 7-9
        chunks[3].Days.Should().Be(1); // Jan 10
    }

    #endregion

    #region Enumeration

    [Fact]
    public void GetEnumerator_EnumeratesAllDates()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 5));

        var dates = range.ToList();

        dates.Should().HaveCount(5);
        dates[0].Should().Be(new LocalDate(2024, 1, 1));
        dates[4].Should().Be(new LocalDate(2024, 1, 5));
    }

    [Fact]
    public void Weekdays_ReturnsOnlyWeekdays()
    {
        // Jan 1, 2024 is Monday
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 7));

        var weekdays = range.Weekdays().ToList();

        weekdays.Should().HaveCount(5); // Mon-Fri
    }

    [Fact]
    public void Weekends_ReturnsOnlyWeekends()
    {
        // Jan 1, 2024 is Monday
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 7));

        var weekends = range.Weekends().ToList();

        weekends.Should().HaveCount(2); // Sat, Sun
    }

    #endregion

    #region Parsing

    [Fact]
    public void Parse_ValidFormat_ReturnsRange()
    {
        var range = DateRange.Parse("2024-01-01/2024-12-31");

        range.Start.Should().Be(new LocalDate(2024, 1, 1));
        range.End.Should().Be(new LocalDate(2024, 12, 31));
    }

    [Fact]
    public void Parse_InvalidFormat_Throws()
    {
        var act = () => DateRange.Parse("invalid");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_ValidFormat_ReturnsTrue()
    {
        var success = DateRange.TryParse("2024-01-01/2024-12-31", out var range);

        success.Should().BeTrue();
        range.Start.Should().Be(new LocalDate(2024, 1, 1));
    }

    [Fact]
    public void TryParse_InvalidFormat_ReturnsFalse()
    {
        var success = DateRange.TryParse("invalid", out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void ToString_ReturnsIsoFormat()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31));

        range.ToString().Should().Be("2024-01-01/2024-12-31");
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_SameRange_ReturnsTrue()
    {
        var range1 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31));
        var range2 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31));

        range1.Equals(range2).Should().BeTrue();
        (range1 == range2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentRange_ReturnsFalse()
    {
        var range1 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31));
        var range2 = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 6, 30));

        range1.Equals(range2).Should().BeFalse();
        (range1 != range2).Should().BeTrue();
    }

    [Fact]
    public void Deconstruct_ReturnsComponents()
    {
        var range = new DateRange(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31));

        var (start, end) = range;

        start.Should().Be(new LocalDate(2024, 1, 1));
        end.Should().Be(new LocalDate(2024, 12, 31));
    }

    #endregion
}
