using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Calculator;
using Pragmatic.Temporal.Holidays;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Tests for <see cref="TemporalCalculator" /> ensuring O(1) business day calculations.
/// </summary>
public class TemporalCalculatorTests
{
    private readonly TemporalCalculator _calculator = new();

    #region CountBusinessDays - O(1) Formula Tests

    [Fact]
    public void CountBusinessDays_SameDay_ReturnsZero()
    {
        var date = new LocalDate(2024, 1, 15); // Monday
        _calculator.CountBusinessDays(date, date).Should().Be(0);
    }

    [Fact]
    public void CountBusinessDays_FromAfterTo_ReturnsZero()
    {
        var from = new LocalDate(2024, 1, 16);
        var to = new LocalDate(2024, 1, 15);
        _calculator.CountBusinessDays(from, to).Should().Be(0);
    }

    [Fact]
    public void CountBusinessDays_OneWeek_ReturnsFiveBusinessDays()
    {
        // Monday Jan 15 to Monday Jan 22 = 5 business days
        var from = new LocalDate(2024, 1, 15); // Monday
        var to = new LocalDate(2024, 1, 22); // Monday
        _calculator.CountBusinessDays(from, to).Should().Be(5);
    }

    [Fact]
    public void CountBusinessDays_TwoWeeks_ReturnsTenBusinessDays()
    {
        // Monday Jan 15 to Monday Jan 29 = 10 business days
        var from = new LocalDate(2024, 1, 15); // Monday
        var to = new LocalDate(2024, 1, 29); // Monday
        _calculator.CountBusinessDays(from, to).Should().Be(10);
    }

    [Fact]
    public void CountBusinessDays_MondayToFriday_ReturnsFourDays()
    {
        // Monday Jan 15 to Friday Jan 19 = 4 business days (Tue, Wed, Thu, Fri)
        var from = new LocalDate(2024, 1, 15); // Monday
        var to = new LocalDate(2024, 1, 19); // Friday
        _calculator.CountBusinessDays(from, to).Should().Be(4);
    }

    [Fact]
    public void CountBusinessDays_FridayToMonday_ReturnsOneDay()
    {
        // Friday Jan 19 to Monday Jan 22 = 1 business day (Monday only)
        var from = new LocalDate(2024, 1, 19); // Friday
        var to = new LocalDate(2024, 1, 22); // Monday
        _calculator.CountBusinessDays(from, to).Should().Be(1);
    }

    [Fact]
    public void CountBusinessDays_SaturdayToMonday_ReturnsZero()
    {
        // Saturday Jan 20 to Monday Jan 22 = 0 business days
        // Range [Sat, Mon) = Sat, Sun - both weekend, so 0
        var from = new LocalDate(2024, 1, 20); // Saturday
        var to = new LocalDate(2024, 1, 22); // Monday
        _calculator.CountBusinessDays(from, to).Should().Be(0);
    }

    [Fact]
    public void CountBusinessDays_FullYear_ReturnsCorrectCount()
    {
        // 2024 has 262 business days (leap year, starts Monday)
        var from = new LocalDate(2024, 1, 1);
        var to = new LocalDate(2025, 1, 1);

        // Calculate expected: 366 days - 104 weekend days = 262
        // (52 full weeks = 104 weekend days, +2 extra days in Jan 2024)
        var result = _calculator.CountBusinessDays(from, to);

        // Should be around 262 for a leap year
        result.Should().BeGreaterThan(250);
        result.Should().BeLessThan(270);
    }

    [Theory]
    [InlineData(2024, 1, 1, 2024, 1, 2, 1)] // Mon-Tue = 1
    [InlineData(2024, 1, 1, 2024, 1, 3, 2)] // Mon-Wed = 2
    [InlineData(2024, 1, 1, 2024, 1, 4, 3)] // Mon-Thu = 3
    [InlineData(2024, 1, 1, 2024, 1, 5, 4)] // Mon-Fri = 4
    [InlineData(2024, 1, 1, 2024, 1, 6, 5)] // Mon-Sat = 5 (Sat doesn't count)
    [InlineData(2024, 1, 1, 2024, 1, 7, 5)] // Mon-Sun = 5 (Sun doesn't count)
    [InlineData(2024, 1, 1, 2024, 1, 8, 5)] // Mon-Mon = 5
    public void CountBusinessDays_VariousPeriods_ReturnsExpectedCount(
        int fromYear, int fromMonth, int fromDay,
        int toYear, int toMonth, int toDay,
        int expected)
    {
        var from = new LocalDate(fromYear, fromMonth, fromDay);
        var to = new LocalDate(toYear, toMonth, toDay);
        _calculator.CountBusinessDays(from, to).Should().Be(expected);
    }

    #endregion

    #region AddBusinessDays - O(1) Formula Tests

    [Fact]
    public void AddBusinessDays_Zero_ReturnsSameDate()
    {
        var date = new LocalDate(2024, 1, 15);
        _calculator.AddBusinessDays(date, 0).Should().Be(date);
    }

    [Fact]
    public void AddBusinessDays_OneDay_FromMonday_ReturnsTuesday()
    {
        var monday = new LocalDate(2024, 1, 15); // Monday
        _calculator.AddBusinessDays(monday, 1).Should().Be(new LocalDate(2024, 1, 16));
    }

    [Fact]
    public void AddBusinessDays_OneDay_FromFriday_ReturnsMonday()
    {
        var friday = new LocalDate(2024, 1, 19); // Friday
        _calculator.AddBusinessDays(friday, 1).Should().Be(new LocalDate(2024, 1, 22)); // Monday
    }

    [Fact]
    public void AddBusinessDays_FiveDays_FromMonday_ReturnsNextMonday()
    {
        var monday = new LocalDate(2024, 1, 15); // Monday
        _calculator.AddBusinessDays(monday, 5).Should().Be(new LocalDate(2024, 1, 22)); // Monday
    }

    [Fact]
    public void AddBusinessDays_TenDays_FromMonday_ReturnsSecondNextMonday()
    {
        var monday = new LocalDate(2024, 1, 15); // Monday
        _calculator.AddBusinessDays(monday, 10).Should().Be(new LocalDate(2024, 1, 29)); // Monday
    }

    [Fact]
    public void AddBusinessDays_NegativeOneDay_FromMonday_ReturnsFriday()
    {
        var monday = new LocalDate(2024, 1, 15); // Monday
        _calculator.AddBusinessDays(monday, -1).Should().Be(new LocalDate(2024, 1, 12)); // Friday
    }

    [Fact]
    public void AddBusinessDays_NegativeFiveDays_FromMonday_ReturnsPreviousMonday()
    {
        var monday = new LocalDate(2024, 1, 15); // Monday
        _calculator.AddBusinessDays(monday, -5).Should().Be(new LocalDate(2024, 1, 8)); // Monday
    }

    [Fact]
    public void AddBusinessDays_FromWeekend_SkipsToWeekday()
    {
        var saturday = new LocalDate(2024, 1, 20); // Saturday
        _calculator.AddBusinessDays(saturday, 1).Should().Be(new LocalDate(2024, 1, 22)); // Monday
    }

    [Theory]
    [InlineData(2024, 1, 15, 1, 2024, 1, 16)]   // Mon +1 = Tue
    [InlineData(2024, 1, 15, 2, 2024, 1, 17)]   // Mon +2 = Wed
    [InlineData(2024, 1, 15, 3, 2024, 1, 18)]   // Mon +3 = Thu
    [InlineData(2024, 1, 15, 4, 2024, 1, 19)]   // Mon +4 = Fri
    [InlineData(2024, 1, 15, 5, 2024, 1, 22)]   // Mon +5 = Mon (skip weekend)
    [InlineData(2024, 1, 15, 6, 2024, 1, 23)]   // Mon +6 = Tue
    [InlineData(2024, 1, 19, 1, 2024, 1, 22)]   // Fri +1 = Mon (skip weekend)
    [InlineData(2024, 1, 19, 3, 2024, 1, 24)]   // Fri +3 = Wed
    public void AddBusinessDays_VariousScenarios_ReturnsExpectedDate(
        int fromYear, int fromMonth, int fromDay,
        int daysToAdd,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var from = new LocalDate(fromYear, fromMonth, fromDay);
        var expected = new LocalDate(expectedYear, expectedMonth, expectedDay);
        _calculator.AddBusinessDays(from, daysToAdd).Should().Be(expected);
    }

    #endregion

    #region IsBusinessDay Tests

    [Theory]
    [InlineData(2024, 1, 15, true)]  // Monday
    [InlineData(2024, 1, 16, true)]  // Tuesday
    [InlineData(2024, 1, 17, true)]  // Wednesday
    [InlineData(2024, 1, 18, true)]  // Thursday
    [InlineData(2024, 1, 19, true)]  // Friday
    [InlineData(2024, 1, 20, false)] // Saturday
    [InlineData(2024, 1, 21, false)] // Sunday
    public void IsBusinessDay_ReturnsCorrectResult(int year, int month, int day, bool expected)
    {
        var date = new LocalDate(year, month, day);
        _calculator.IsBusinessDay(date).Should().Be(expected);
    }

    #endregion

    #region IsWeekend Tests

    [Theory]
    [InlineData(2024, 1, 15, false)] // Monday
    [InlineData(2024, 1, 20, true)]  // Saturday
    [InlineData(2024, 1, 21, true)]  // Sunday
    public void IsWeekend_ReturnsCorrectResult(int year, int month, int day, bool expected)
    {
        var date = new LocalDate(year, month, day);
        _calculator.IsWeekend(date).Should().Be(expected);
    }

    #endregion

    #region NextBusinessDay Tests

    [Fact]
    public void NextBusinessDay_FromMonday_ReturnsTuesday()
    {
        var monday = new LocalDate(2024, 1, 15);
        _calculator.NextBusinessDay(monday).Should().Be(new LocalDate(2024, 1, 16));
    }

    [Fact]
    public void NextBusinessDay_FromFriday_ReturnsMonday()
    {
        var friday = new LocalDate(2024, 1, 19);
        _calculator.NextBusinessDay(friday).Should().Be(new LocalDate(2024, 1, 22));
    }

    [Fact]
    public void NextBusinessDay_FromSaturday_ReturnsMonday()
    {
        var saturday = new LocalDate(2024, 1, 20);
        _calculator.NextBusinessDay(saturday).Should().Be(new LocalDate(2024, 1, 22));
    }

    [Fact]
    public void NextBusinessDay_FromSunday_ReturnsMonday()
    {
        var sunday = new LocalDate(2024, 1, 21);
        _calculator.NextBusinessDay(sunday).Should().Be(new LocalDate(2024, 1, 22));
    }

    #endregion

    #region PreviousBusinessDay Tests

    [Fact]
    public void PreviousBusinessDay_FromTuesday_ReturnsMonday()
    {
        var tuesday = new LocalDate(2024, 1, 16);
        _calculator.PreviousBusinessDay(tuesday).Should().Be(new LocalDate(2024, 1, 15));
    }

    [Fact]
    public void PreviousBusinessDay_FromMonday_ReturnsFriday()
    {
        var monday = new LocalDate(2024, 1, 15);
        _calculator.PreviousBusinessDay(monday).Should().Be(new LocalDate(2024, 1, 12));
    }

    [Fact]
    public void PreviousBusinessDay_FromSunday_ReturnsFriday()
    {
        var sunday = new LocalDate(2024, 1, 21);
        _calculator.PreviousBusinessDay(sunday).Should().Be(new LocalDate(2024, 1, 19));
    }

    [Fact]
    public void PreviousBusinessDay_FromSaturday_ReturnsFriday()
    {
        var saturday = new LocalDate(2024, 1, 20);
        _calculator.PreviousBusinessDay(saturday).Should().Be(new LocalDate(2024, 1, 19));
    }

    #endregion

    #region Period Navigation Tests

    [Fact]
    public void StartOfWeek_ReturnsCorrectMonday()
    {
        var wednesday = new LocalDate(2024, 1, 17);
        _calculator.StartOfWeek(wednesday).Should().Be(new LocalDate(2024, 1, 15));
    }

    [Fact]
    public void EndOfWeek_ReturnsCorrectSunday()
    {
        var wednesday = new LocalDate(2024, 1, 17);
        _calculator.EndOfWeek(wednesday).Should().Be(new LocalDate(2024, 1, 21));
    }

    [Fact]
    public void StartOfMonth_ReturnsFirstDay()
    {
        var midMonth = new LocalDate(2024, 1, 17);
        _calculator.StartOfMonth(midMonth).Should().Be(new LocalDate(2024, 1, 1));
    }

    [Fact]
    public void EndOfMonth_ReturnsLastDay()
    {
        var midMonth = new LocalDate(2024, 1, 17);
        _calculator.EndOfMonth(midMonth).Should().Be(new LocalDate(2024, 1, 31));
    }

    [Fact]
    public void EndOfMonth_February_LeapYear_Returns29()
    {
        var february = new LocalDate(2024, 2, 15);
        _calculator.EndOfMonth(february).Should().Be(new LocalDate(2024, 2, 29));
    }

    [Fact]
    public void EndOfMonth_February_NonLeapYear_Returns28()
    {
        var february = new LocalDate(2023, 2, 15);
        _calculator.EndOfMonth(february).Should().Be(new LocalDate(2023, 2, 28));
    }

    [Fact]
    public void StartOfQuarter_Q1_ReturnsJanuary1()
    {
        var february = new LocalDate(2024, 2, 15);
        _calculator.StartOfQuarter(february).Should().Be(new LocalDate(2024, 1, 1));
    }

    [Fact]
    public void EndOfQuarter_Q1_ReturnsMarch31()
    {
        var february = new LocalDate(2024, 2, 15);
        _calculator.EndOfQuarter(february).Should().Be(new LocalDate(2024, 3, 31));
    }

    [Fact]
    public void StartOfYear_ReturnsJanuary1()
    {
        var midYear = new LocalDate(2024, 6, 15);
        _calculator.StartOfYear(midYear).Should().Be(new LocalDate(2024, 1, 1));
    }

    [Fact]
    public void EndOfYear_ReturnsDecember31()
    {
        var midYear = new LocalDate(2024, 6, 15);
        _calculator.EndOfYear(midYear).Should().Be(new LocalDate(2024, 12, 31));
    }

    #endregion

    #region Holiday-Aware Tests

    [Fact]
    public void CountBusinessDays_WithHolidays_SubtractsWeekdayHolidays()
    {
        // Create a provider with a holiday on Wednesday Jan 17
        var holidayProvider = new TestHolidayProvider(new LocalDate(2024, 1, 17));
        var calculator = new TemporalCalculator(holidayProvider);

        // Monday Jan 15 to Friday Jan 19 normally = 4 business days
        // But with holiday on Wed = 3 business days
        var from = new LocalDate(2024, 1, 15);
        var to = new LocalDate(2024, 1, 19);
        calculator.CountBusinessDays(from, to, "IT").Should().Be(3);
    }

    /// <summary>
    ///     <c>from</c> is inclusive: a holiday on it is a day not worked.
    /// </summary>
    /// <remarks>
    ///     The holidays were filtered on <c>(from, to]</c> while the days were counted on
    ///     <c>[from, to)</c>. A holiday in the middle of the range, as in the test above, cannot tell the
    ///     two apart; one on a bound can.
    /// </remarks>
    [Fact]
    public void CountBusinessDays_AHolidayOnTheFirstDay_IsSubtracted()
    {
        var calculator = new TemporalCalculator(new TestHolidayProvider(new LocalDate(2024, 1, 15)));

        // Mon 15 [holiday], Tue 16, Wed 17, Thu 18 — Fri 19 excluded.
        calculator.CountBusinessDays(new LocalDate(2024, 1, 15), new LocalDate(2024, 1, 19), "IT").Should().Be(3);
    }

    /// <summary><c>to</c> is exclusive: a holiday on it was never counted, and takes nothing away.</summary>
    [Fact]
    public void CountBusinessDays_AHolidayOnTheExcludedEnd_IsNotSubtracted()
    {
        var calculator = new TemporalCalculator(new TestHolidayProvider(new LocalDate(2024, 1, 19)));

        // Mon 15, Tue 16, Wed 17, Thu 18 — Fri 19 [holiday] excluded anyway.
        calculator.CountBusinessDays(new LocalDate(2024, 1, 15), new LocalDate(2024, 1, 19), "IT").Should().Be(4);
    }

    [Fact]
    public void AddBusinessDays_WithHolidays_SkipsHolidays()
    {
        // Create a provider with a holiday on Tuesday Jan 16
        var holidayProvider = new TestHolidayProvider(new LocalDate(2024, 1, 16));
        var calculator = new TemporalCalculator(holidayProvider);

        // Monday + 1 business day with Tuesday holiday = Wednesday
        var monday = new LocalDate(2024, 1, 15);
        calculator.AddBusinessDays(monday, 1, "IT").Should().Be(new LocalDate(2024, 1, 17));
    }

    [Fact]
    public void IsBusinessDay_WithHoliday_ReturnsFalse()
    {
        var holidayProvider = new TestHolidayProvider(new LocalDate(2024, 1, 17));
        var calculator = new TemporalCalculator(holidayProvider);

        var holiday = new LocalDate(2024, 1, 17);
        calculator.IsBusinessDay(holiday, "IT").Should().BeFalse();
    }

    [Fact]
    public void IsHoliday_WithHoliday_ReturnsTrue()
    {
        var holidayProvider = new TestHolidayProvider(new LocalDate(2024, 1, 17));
        var calculator = new TemporalCalculator(holidayProvider);

        var holiday = new LocalDate(2024, 1, 17);
        calculator.IsHoliday(holiday, "IT").Should().BeTrue();
    }

    [Fact]
    public void NextBusinessDay_WithHoliday_SkipsHoliday()
    {
        // Holiday on Tuesday
        var holidayProvider = new TestHolidayProvider(new LocalDate(2024, 1, 16));
        var calculator = new TemporalCalculator(holidayProvider);

        // Monday next business day with Tuesday holiday = Wednesday
        var monday = new LocalDate(2024, 1, 15);
        calculator.NextBusinessDay(monday, "IT").Should().Be(new LocalDate(2024, 1, 17));
    }

    [Fact]
    public void PreviousBusinessDay_WithHoliday_SkipsHoliday()
    {
        // Holiday on Wednesday
        var holidayProvider = new TestHolidayProvider(new LocalDate(2024, 1, 17));
        var calculator = new TemporalCalculator(holidayProvider);

        // Thursday previous business day with Wednesday holiday = Tuesday
        var thursday = new LocalDate(2024, 1, 18);
        calculator.PreviousBusinessDay(thursday, "IT").Should().Be(new LocalDate(2024, 1, 16));
    }

    #endregion

    #region Custom Holiday List Tests

    [Fact]
    public void AddBusinessDays_WithCustomHolidayList_SkipsHolidays()
    {
        var holidays = new List<LocalDate>
        {
            new(2024, 1, 16), // Tuesday
            new(2024, 1, 17)  // Wednesday
        };

        // Monday + 1 business day with Tue & Wed holidays = Thursday
        var monday = new LocalDate(2024, 1, 15);
        _calculator.AddBusinessDays(monday, 1, holidays).Should().Be(new LocalDate(2024, 1, 18));
    }

    [Fact]
    public void AddBusinessDays_WithCustomHolidayOnWeekend_NotCounted()
    {
        var holidays = new List<LocalDate>
        {
            new(2024, 1, 20) // Saturday - should not affect count
        };

        // Friday + 1 business day = Monday (Saturday holiday doesn't matter)
        var friday = new LocalDate(2024, 1, 19);
        _calculator.AddBusinessDays(friday, 1, holidays).Should().Be(new LocalDate(2024, 1, 22));
    }

    #endregion

    /// <summary>
    ///     Simple test holiday provider for unit tests.
    /// </summary>
    private sealed class TestHolidayProvider(params LocalDate[] holidays) : IHolidayProvider
    {
        private readonly HashSet<LocalDate> _holidays = holidays.ToHashSet();

        public IEnumerable<string> SupportedCountries => ["IT"];

        public bool IsHoliday(LocalDate date, string countryCode) => _holidays.Contains(date);

        public IEnumerable<Holiday> GetHolidays(int year, string countryCode)
        {
            return _holidays
                .Where(h => h.Year == year)
                .Select(h => new Holiday(h, "Test Holiday", HolidayType.Public));
        }

        public IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode)
        {
            return GetHolidays(year, countryCode);
        }
    }
}
