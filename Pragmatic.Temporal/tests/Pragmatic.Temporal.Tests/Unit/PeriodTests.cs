using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Tests for <see cref="Period" />.
/// </summary>
public class PeriodTests
{
    #region Construction

    [Fact]
    public void Constructor_SetsComponents()
    {
        var period = new Period(1, 2, 3);

        period.Years.Should().Be(1);
        period.Months.Should().Be(2);
        period.Days.Should().Be(3);
    }

    [Fact]
    public void FromYears_CreatesCorrectPeriod()
    {
        var period = Period.FromYears(5);

        period.Years.Should().Be(5);
        period.Months.Should().Be(0);
        period.Days.Should().Be(0);
    }

    [Fact]
    public void FromMonths_CreatesCorrectPeriod()
    {
        var period = Period.FromMonths(6);

        period.Years.Should().Be(0);
        period.Months.Should().Be(6);
        period.Days.Should().Be(0);
    }

    [Fact]
    public void FromDays_CreatesCorrectPeriod()
    {
        var period = Period.FromDays(15);

        period.Years.Should().Be(0);
        period.Months.Should().Be(0);
        period.Days.Should().Be(15);
    }

    [Fact]
    public void FromWeeks_ConvertsToDays()
    {
        var period = Period.FromWeeks(2);

        period.Days.Should().Be(14);
    }

    #endregion

    #region Properties

    [Fact]
    public void IsZero_ZeroPeriod_ReturnsTrue()
    {
        Period.Zero.IsZero.Should().BeTrue();
        new Period(0, 0, 0).IsZero.Should().BeTrue();
    }

    [Fact]
    public void IsZero_NonZeroPeriod_ReturnsFalse()
    {
        new Period(1, 0, 0).IsZero.Should().BeFalse();
        new Period(0, 1, 0).IsZero.Should().BeFalse();
        new Period(0, 0, 1).IsZero.Should().BeFalse();
    }

    [Fact]
    public void IsNegative_NegativeComponent_ReturnsTrue()
    {
        new Period(-1, 0, 0).IsNegative.Should().BeTrue();
        new Period(0, -1, 0).IsNegative.Should().BeTrue();
        new Period(0, 0, -1).IsNegative.Should().BeTrue();
    }

    [Fact]
    public void IsNegative_AllPositive_ReturnsFalse()
    {
        new Period(1, 2, 3).IsNegative.Should().BeFalse();
    }

    [Fact]
    public void TotalMonths_CalculatesCorrectly()
    {
        new Period(1, 6, 0).TotalMonths.Should().Be(18);
        new Period(2, 0, 15).TotalMonths.Should().Be(24);
    }

    #endregion

    #region Between

    [Fact]
    public void Between_SameDate_ReturnsZero()
    {
        var date = new LocalDate(2024, 1, 15);

        var period = Period.Between(date, date);

        period.IsZero.Should().BeTrue();
    }

    [Fact]
    public void Between_OneMonthApart_ReturnsOneMonth()
    {
        var start = new LocalDate(2024, 1, 15);
        var end = new LocalDate(2024, 2, 15);

        var period = Period.Between(start, end);

        period.Should().Be(new Period(0, 1, 0));
    }

    [Fact]
    public void Between_OneYearApart_ReturnsOneYear()
    {
        var start = new LocalDate(2024, 1, 15);
        var end = new LocalDate(2025, 1, 15);

        var period = Period.Between(start, end);

        period.Should().Be(new Period(1, 0, 0));
    }

    [Fact]
    public void Between_ComplexDifference_CalculatesCorrectly()
    {
        var start = new LocalDate(2024, 1, 15);
        var end = new LocalDate(2025, 3, 20);

        var period = Period.Between(start, end);

        period.Years.Should().Be(1);
        period.Months.Should().Be(2);
        period.Days.Should().Be(5);
    }

    [Fact]
    public void Between_EndBeforeStart_ReturnsNegativePeriod()
    {
        var start = new LocalDate(2025, 1, 15);
        var end = new LocalDate(2024, 1, 15);

        var period = Period.Between(start, end);

        period.Years.Should().Be(-1);
    }

    #endregion

    #region Arithmetic

    [Fact]
    public void Add_TwoPeriods_CombinesComponents()
    {
        var p1 = new Period(1, 2, 3);
        var p2 = new Period(2, 3, 4);

        var result = p1 + p2;

        result.Should().Be(new Period(3, 5, 7));
    }

    [Fact]
    public void Subtract_TwoPeriods_SubtractsComponents()
    {
        var p1 = new Period(3, 5, 7);
        var p2 = new Period(1, 2, 3);

        var result = p1 - p2;

        result.Should().Be(new Period(2, 3, 4));
    }

    [Fact]
    public void Negate_ReversesAllComponents()
    {
        var period = new Period(1, 2, 3);

        var negated = -period;

        negated.Should().Be(new Period(-1, -2, -3));
    }

    [Fact]
    public void Multiply_ScalesAllComponents()
    {
        var period = new Period(1, 2, 3);

        var scaled = period * 3;

        scaled.Should().Be(new Period(3, 6, 9));
    }

    [Fact]
    public void Normalize_ConvertsExcessMonthsToYears()
    {
        var period = new Period(0, 14, 5);

        var normalized = period.Normalize();

        normalized.Years.Should().Be(1);
        normalized.Months.Should().Be(2);
        normalized.Days.Should().Be(5);
    }

    [Fact]
    public void Normalize_HandlesNegativeMonths()
    {
        var period = new Period(1, -2, 0);

        var normalized = period.Normalize();

        normalized.Years.Should().Be(0);
        normalized.Months.Should().Be(10);
    }

    #endregion

    #region Parsing

    [Fact]
    public void Parse_YearsOnly_ReturnsCorrectPeriod()
    {
        var period = Period.Parse("P2Y");

        period.Should().Be(new Period(2, 0, 0));
    }

    [Fact]
    public void Parse_MonthsOnly_ReturnsCorrectPeriod()
    {
        var period = Period.Parse("P6M");

        period.Should().Be(new Period(0, 6, 0));
    }

    [Fact]
    public void Parse_DaysOnly_ReturnsCorrectPeriod()
    {
        var period = Period.Parse("P15D");

        period.Should().Be(new Period(0, 0, 15));
    }

    [Fact]
    public void Parse_Full_ReturnsCorrectPeriod()
    {
        var period = Period.Parse("P1Y2M3D");

        period.Should().Be(new Period(1, 2, 3));
    }

    [Fact]
    public void Parse_InvalidFormat_Throws()
    {
        var act = () => Period.Parse("invalid");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_ValidFormat_ReturnsTrue()
    {
        var success = Period.TryParse("P1Y2M3D", out var period);

        success.Should().BeTrue();
        period.Should().Be(new Period(1, 2, 3));
    }

    [Fact]
    public void TryParse_InvalidFormat_ReturnsFalse()
    {
        var success = Period.TryParse("invalid", out _);

        success.Should().BeFalse();
    }

    #endregion

    #region Formatting

    [Fact]
    public void ToString_Full_ReturnsIsoFormat()
    {
        var period = new Period(1, 2, 3);

        period.ToString().Should().Be("P1Y2M3D");
    }

    [Fact]
    public void ToString_YearsOnly_OmitsZeroComponents()
    {
        var period = new Period(2, 0, 0);

        period.ToString().Should().Be("P2Y");
    }

    [Fact]
    public void ToString_Zero_ReturnsP0D()
    {
        Period.Zero.ToString().Should().Be("P0D");
    }

    [Fact]
    public void ToDisplayString_ReturnsHumanReadable()
    {
        var period = new Period(1, 2, 3);

        period.ToDisplayString().Should().Be("1 year, 2 months, 3 days");
    }

    [Fact]
    public void ToDisplayString_SingleValues_UsesSingular()
    {
        var period = new Period(1, 1, 1);

        period.ToDisplayString().Should().Be("1 year, 1 month, 1 day");
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_SamePeriod_ReturnsTrue()
    {
        var p1 = new Period(1, 2, 3);
        var p2 = new Period(1, 2, 3);

        p1.Equals(p2).Should().BeTrue();
        (p1 == p2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentPeriod_ReturnsFalse()
    {
        var p1 = new Period(1, 2, 3);
        var p2 = new Period(1, 2, 4);

        p1.Equals(p2).Should().BeFalse();
        (p1 != p2).Should().BeTrue();
    }

    [Fact]
    public void Deconstruct_ReturnsComponents()
    {
        var period = new Period(1, 2, 3);

        var (years, months, days) = period;

        years.Should().Be(1);
        months.Should().Be(2);
        days.Should().Be(3);
    }

    #endregion
}
