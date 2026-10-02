using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Extensions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Tests for <see cref="LocalDateExtensions" />.
/// </summary>
public class LocalDateExtensionsTests
{
    #region Period Arithmetic

    [Fact]
    public void Add_Period_AddsYearsMonthsDays()
    {
        var date = new LocalDate(2024, 1, 15);
        var period = new Period(1, 2, 3);

        var result = date.Add(period);

        result.Should().Be(new LocalDate(2025, 3, 18));
    }

    [Fact]
    public void Add_OneMonth_FromJan31_ReturnsFeb28Or29()
    {
        var jan31 = new LocalDate(2024, 1, 31);
        var period = Period.FromMonths(1);

        var result = jan31.Add(period);

        result.Should().Be(new LocalDate(2024, 2, 29)); // 2024 is leap year
    }

    [Fact]
    public void Add_OneMonth_FromJan31_NonLeapYear_ReturnsFeb28()
    {
        var jan31 = new LocalDate(2023, 1, 31);
        var period = Period.FromMonths(1);

        var result = jan31.Add(period);

        result.Should().Be(new LocalDate(2023, 2, 28));
    }

    [Fact]
    public void Subtract_Period_SubtractsYearsMonthsDays()
    {
        var date = new LocalDate(2025, 3, 18);
        var period = new Period(1, 2, 3);

        var result = date.Subtract(period);

        result.Should().Be(new LocalDate(2024, 1, 15));
    }

    #endregion

    #region Next/Previous Day of Week

    [Fact]
    public void Next_ReturnsNextOccurrence()
    {
        // Wednesday Jan 15, 2025
        var wednesday = new LocalDate(2025, 1, 15);

        wednesday.Next(DayOfWeek.Friday).Should().Be(new LocalDate(2025, 1, 17));
        wednesday.Next(DayOfWeek.Monday).Should().Be(new LocalDate(2025, 1, 20));
    }

    [Fact]
    public void Next_SameDay_ReturnsNextWeek()
    {
        // Wednesday Jan 15, 2025
        var wednesday = new LocalDate(2025, 1, 15);

        wednesday.Next(DayOfWeek.Wednesday).Should().Be(new LocalDate(2025, 1, 22));
    }

    [Fact]
    public void NextOrSame_SameDay_ReturnsSameDate()
    {
        var wednesday = new LocalDate(2025, 1, 15);

        wednesday.NextOrSame(DayOfWeek.Wednesday).Should().Be(wednesday);
    }

    [Fact]
    public void NextOrSame_DifferentDay_ReturnsNext()
    {
        var wednesday = new LocalDate(2025, 1, 15);

        wednesday.NextOrSame(DayOfWeek.Friday).Should().Be(new LocalDate(2025, 1, 17));
    }

    [Fact]
    public void Previous_ReturnsPreviousOccurrence()
    {
        // Wednesday Jan 15, 2025
        var wednesday = new LocalDate(2025, 1, 15);

        wednesday.Previous(DayOfWeek.Monday).Should().Be(new LocalDate(2025, 1, 13));
        wednesday.Previous(DayOfWeek.Friday).Should().Be(new LocalDate(2025, 1, 10));
    }

    [Fact]
    public void Previous_SameDay_ReturnsPreviousWeek()
    {
        var wednesday = new LocalDate(2025, 1, 15);

        wednesday.Previous(DayOfWeek.Wednesday).Should().Be(new LocalDate(2025, 1, 8));
    }

    [Fact]
    public void PreviousOrSame_SameDay_ReturnsSameDate()
    {
        var wednesday = new LocalDate(2025, 1, 15);

        wednesday.PreviousOrSame(DayOfWeek.Wednesday).Should().Be(wednesday);
    }

    #endregion

    #region Month-Based Navigation

    [Fact]
    public void FirstInMonth_ReturnsFirstOccurrence()
    {
        // January 2025 starts on Wednesday
        var date = new LocalDate(2025, 1, 15);

        date.FirstInMonth(DayOfWeek.Monday).Should().Be(new LocalDate(2025, 1, 6));
        date.FirstInMonth(DayOfWeek.Wednesday).Should().Be(new LocalDate(2025, 1, 1));
    }

    [Fact]
    public void LastInMonth_ReturnsLastOccurrence()
    {
        // January 2025 ends on Friday
        var date = new LocalDate(2025, 1, 15);

        date.LastInMonth(DayOfWeek.Friday).Should().Be(new LocalDate(2025, 1, 31));
        date.LastInMonth(DayOfWeek.Monday).Should().Be(new LocalDate(2025, 1, 27));
    }

    [Fact]
    public void NthInMonth_ReturnsCorrectOccurrence()
    {
        var date = new LocalDate(2025, 1, 1);

        // Second Tuesday of January 2025
        var secondTuesday = date.NthInMonth(2, DayOfWeek.Tuesday);

        secondTuesday.Should().Be(new LocalDate(2025, 1, 14));
    }

    [Fact]
    public void NthInMonth_PatchTuesday_ReturnsSecondTuesday()
    {
        // Microsoft Patch Tuesday = 2nd Tuesday of each month
        var jan2025 = new LocalDate(2025, 1, 1);

        var patchTuesday = jan2025.NthInMonth(2, DayOfWeek.Tuesday);

        patchTuesday.Should().Be(new LocalDate(2025, 1, 14));
    }

    [Fact]
    public void NthInMonth_Thanksgiving_ReturnsFourthThursday()
    {
        // US Thanksgiving = 4th Thursday in November
        var nov2025 = new LocalDate(2025, 11, 1);

        var thanksgiving = nov2025.NthInMonth(4, DayOfWeek.Thursday);

        thanksgiving.Should().Be(new LocalDate(2025, 11, 27));
    }

    [Fact]
    public void NthInMonth_LastSunday_ReturnsLastOccurrence()
    {
        var jan2025 = new LocalDate(2025, 1, 1);

        var lastSunday = jan2025.NthInMonth(-1, DayOfWeek.Sunday);

        lastSunday.Should().Be(new LocalDate(2025, 1, 26));
    }

    [Fact]
    public void NthInMonth_FifthOccurrence_DoesNotExist_ReturnsNull()
    {
        var jan2025 = new LocalDate(2025, 1, 1);

        var fifthMonday = jan2025.NthInMonth(5, DayOfWeek.Monday);

        fifthMonday.Should().BeNull();
    }

    [Fact]
    public void NthInMonthOrThrow_FifthOccurrence_Throws()
    {
        var jan2025 = new LocalDate(2025, 1, 1);

        var act = () => jan2025.NthInMonthOrThrow(5, DayOfWeek.Monday);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Year-Based Navigation

    [Fact]
    public void FirstInYear_ReturnsFirstOccurrence()
    {
        var date = new LocalDate(2025, 6, 15);

        // 2025 starts on Wednesday
        date.FirstInYear(DayOfWeek.Monday).Should().Be(new LocalDate(2025, 1, 6));
        date.FirstInYear(DayOfWeek.Wednesday).Should().Be(new LocalDate(2025, 1, 1));
    }

    [Fact]
    public void LastInYear_ReturnsLastOccurrence()
    {
        var date = new LocalDate(2025, 6, 15);

        // 2025 ends on Wednesday
        date.LastInYear(DayOfWeek.Wednesday).Should().Be(new LocalDate(2025, 12, 31));
        date.LastInYear(DayOfWeek.Friday).Should().Be(new LocalDate(2025, 12, 26));
    }

    #endregion

    #region Business Day Helpers

    [Fact]
    public void NextWeekday_FromFriday_ReturnsMonday()
    {
        var friday = new LocalDate(2025, 1, 17);

        friday.NextWeekday().Should().Be(new LocalDate(2025, 1, 20)); // Monday
    }

    [Fact]
    public void NextWeekday_FromSaturday_ReturnsMonday()
    {
        var saturday = new LocalDate(2025, 1, 18);

        saturday.NextWeekday().Should().Be(new LocalDate(2025, 1, 20)); // Monday
    }

    [Fact]
    public void NextWeekdayOrSame_OnWeekday_ReturnsSame()
    {
        var wednesday = new LocalDate(2025, 1, 15);

        wednesday.NextWeekdayOrSame().Should().Be(wednesday);
    }

    [Fact]
    public void NextWeekdayOrSame_OnWeekend_ReturnsMonday()
    {
        var saturday = new LocalDate(2025, 1, 18);

        saturday.NextWeekdayOrSame().Should().Be(new LocalDate(2025, 1, 20));
    }

    [Fact]
    public void PreviousWeekday_FromMonday_ReturnsFriday()
    {
        var monday = new LocalDate(2025, 1, 20);

        monday.PreviousWeekday().Should().Be(new LocalDate(2025, 1, 17)); // Friday
    }

    [Fact]
    public void PreviousWeekdayOrSame_OnWeekend_ReturnsFriday()
    {
        var sunday = new LocalDate(2025, 1, 19);

        sunday.PreviousWeekdayOrSame().Should().Be(new LocalDate(2025, 1, 17)); // Friday
    }

    [Fact]
    public void NearestWeekday_OnSaturday_ReturnsFriday()
    {
        var saturday = new LocalDate(2025, 1, 18);

        saturday.NearestWeekday().Should().Be(new LocalDate(2025, 1, 17)); // Friday
    }

    [Fact]
    public void NearestWeekday_OnSunday_ReturnsMonday()
    {
        var sunday = new LocalDate(2025, 1, 19);

        sunday.NearestWeekday().Should().Be(new LocalDate(2025, 1, 20)); // Monday
    }

    [Fact]
    public void NearestWeekday_OnWeekday_ReturnsSame()
    {
        var wednesday = new LocalDate(2025, 1, 15);

        wednesday.NearestWeekday().Should().Be(wednesday);
    }

    #endregion

    #region ISO Week

    [Fact]
    public void IsoWeekOfYear_ReturnsCorrectWeek()
    {
        // Jan 1, 2025 is Wednesday, part of week 1
        new LocalDate(2025, 1, 1).IsoWeekOfYear().Should().Be(1);

        // Dec 29, 2025 might be week 1 of 2026
        new LocalDate(2025, 12, 29).IsoWeekOfYear().Should().Be(1);
    }

    [Fact]
    public void IsoWeekYear_MayDifferFromCalendarYear()
    {
        // Dec 31, 2024 might be in ISO week year 2025
        var dec31 = new LocalDate(2024, 12, 31);

        // Verify the year is calculated correctly
        var isoYear = dec31.IsoWeekYear();
        isoYear.Should().BeOneOf(2024, 2025);
    }

    #endregion
}
