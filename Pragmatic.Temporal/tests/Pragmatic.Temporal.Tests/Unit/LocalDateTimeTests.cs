using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

public class LocalDateTimeTests
{
    #region Constructor

    [Fact]
    public void Constructor_FromDateAndTime_CreatesInstance()
    {
        var date = new LocalDate(2024, 6, 15);
        var time = new LocalTime(14, 30);

        var dateTime = new LocalDateTime(date, time);

        dateTime.Year.Should().Be(2024);
        dateTime.Month.Should().Be(6);
        dateTime.Day.Should().Be(15);
        dateTime.Hour.Should().Be(14);
        dateTime.Minute.Should().Be(30);
    }

    [Fact]
    public void Constructor_FromComponents_CreatesInstance()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30, 45);

        dateTime.Year.Should().Be(2024);
        dateTime.Month.Should().Be(6);
        dateTime.Day.Should().Be(15);
        dateTime.Hour.Should().Be(14);
        dateTime.Minute.Should().Be(30);
        dateTime.Second.Should().Be(45);
    }

    [Fact]
    public void Constructor_FromComponentsWithDefaultSecond_CreatesInstance()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        dateTime.Second.Should().Be(0);
    }

    [Fact]
    public void Constructor_FromDateTime_CreatesInstance()
    {
        var dt = new DateTime(2024, 6, 15, 14, 30, 45, DateTimeKind.Utc);

        var dateTime = new LocalDateTime(dt);

        dateTime.Year.Should().Be(2024);
        dateTime.Month.Should().Be(6);
        dateTime.Day.Should().Be(15);
        dateTime.Hour.Should().Be(14);
        dateTime.Minute.Should().Be(30);
        dateTime.Second.Should().Be(45);
    }

    #endregion

    #region Properties

    [Fact]
    public void Date_ReturnsLocalDate()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        dateTime.Date.Should().Be(new LocalDate(2024, 6, 15));
    }

    [Fact]
    public void Time_ReturnsLocalTime()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30, 45);

        dateTime.Time.Hour.Should().Be(14);
        dateTime.Time.Minute.Should().Be(30);
        dateTime.Time.Second.Should().Be(45);
    }

    [Fact]
    public void DayOfWeek_ReturnsCorrectDay()
    {
        // June 15, 2024 is a Saturday
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        dateTime.DayOfWeek.Should().Be(DayOfWeek.Saturday);
    }

    [Fact]
    public void DayOfYear_ReturnsCorrectDay()
    {
        // January 15 is the 15th day of the year
        var dateTime = new LocalDateTime(2024, 1, 15, 0, 0);

        dateTime.DayOfYear.Should().Be(15);
    }

    [Fact]
    public void MinValue_IsMinDateTime()
    {
        LocalDateTime.MinValue.Year.Should().Be(1);
        LocalDateTime.MinValue.Month.Should().Be(1);
        LocalDateTime.MinValue.Day.Should().Be(1);
    }

    [Fact]
    public void MaxValue_IsMaxDateTime()
    {
        LocalDateTime.MaxValue.Year.Should().Be(9999);
        LocalDateTime.MaxValue.Month.Should().Be(12);
        LocalDateTime.MaxValue.Day.Should().Be(31);
    }

    #endregion

    #region Arithmetic

    [Fact]
    public void Add_WithDuration_ReturnsCorrectDateTime()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);
        var duration = Duration.FromHours(2);

        var result = dateTime.Add(duration);

        result.Hour.Should().Be(16);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void Add_WithTimeSpan_ReturnsCorrectDateTime()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        var result = dateTime.Add(TimeSpan.FromHours(2));

        result.Hour.Should().Be(16);
    }

    [Fact]
    public void AddDays_ReturnsCorrectDateTime()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        var result = dateTime.AddDays(10);

        result.Day.Should().Be(25);
        result.Hour.Should().Be(14);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void AddMonths_ReturnsCorrectDateTime()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        var result = dateTime.AddMonths(3);

        result.Month.Should().Be(9);
        result.Day.Should().Be(15);
    }

    [Fact]
    public void AddYears_ReturnsCorrectDateTime()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        var result = dateTime.AddYears(2);

        result.Year.Should().Be(2026);
    }

    [Fact]
    public void AddHours_ReturnsCorrectDateTime()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        var result = dateTime.AddHours(3);

        result.Hour.Should().Be(17);
    }

    [Fact]
    public void AddMinutes_ReturnsCorrectDateTime()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        var result = dateTime.AddMinutes(45);

        result.Hour.Should().Be(15);
        result.Minute.Should().Be(15);
    }

    [Fact]
    public void AddSeconds_ReturnsCorrectDateTime()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        var result = dateTime.AddSeconds(90);

        result.Minute.Should().Be(31);
        result.Second.Should().Be(30);
    }

    [Fact]
    public void DurationUntil_ReturnsCorrectDuration()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 10, 0);
        var dt2 = new LocalDateTime(2024, 6, 15, 14, 30);

        var duration = dt1.DurationUntil(dt2);

        duration.TotalHours.Should().Be(4.5);
    }

    [Fact]
    public void DurationUntil_AcrossDays_ReturnsCorrectDuration()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 22, 0);
        var dt2 = new LocalDateTime(2024, 6, 16, 2, 0);

        var duration = dt1.DurationUntil(dt2);

        duration.TotalHours.Should().Be(4);
    }

    #endregion

    #region Navigation

    [Fact]
    public void StartOfDay_ReturnsMidnight()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30, 45);

        var result = dateTime.StartOfDay();

        result.Hour.Should().Be(0);
        result.Minute.Should().Be(0);
        result.Second.Should().Be(0);
        result.Day.Should().Be(15);
    }

    [Fact]
    public void EndOfDay_ReturnsEndOfDay()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);

        var result = dateTime.EndOfDay();

        result.Hour.Should().Be(23);
        result.Minute.Should().Be(59);
        result.Second.Should().Be(59);
        result.Day.Should().Be(15);
    }

    [Fact]
    public void StartOfHour_ReturnsStartOfHour()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30, 45);

        var result = dateTime.StartOfHour();

        result.Hour.Should().Be(14);
        result.Minute.Should().Be(0);
        result.Second.Should().Be(0);
    }

    #endregion

    #region Conversion

    [Fact]
    public void ToDateTime_ReturnsDateTime()
    {
        var localDt = new LocalDateTime(2024, 6, 15, 14, 30, 45);

        var dt = localDt.ToDateTime();

        dt.Year.Should().Be(2024);
        dt.Month.Should().Be(6);
        dt.Day.Should().Be(15);
        dt.Hour.Should().Be(14);
        dt.Minute.Should().Be(30);
        dt.Second.Should().Be(45);
        dt.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void InZone_WithTimeZoneInfo_ReturnsZonedDateTime()
    {
        var localDt = new LocalDateTime(2024, 6, 15, 14, 30);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        var zonedDt = localDt.InZone(zone);

        zonedDt.LocalDateTime.Should().Be(localDt.ToDateTime());
    }

    [Fact]
    public void InZone_WithTimezoneId_ReturnsZonedDateTime()
    {
        var localDt = new LocalDateTime(2024, 6, 15, 14, 30);

        var zonedDt = localDt.InZone("America/Los_Angeles");

        zonedDt.LocalDateTime.Should().Be(localDt.ToDateTime());
    }

    [Fact]
    public void ToDateTimeOffset_ReturnsCorrectOffset()
    {
        var localDt = new LocalDateTime(2024, 6, 15, 14, 30);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        var dto = localDt.ToDateTimeOffset(zone);

        dto.DateTime.Hour.Should().Be(14);
        dto.DateTime.Minute.Should().Be(30);
    }

    [Fact]
    public void Deconstruct_ReturnsDateAndTime()
    {
        var localDt = new LocalDateTime(2024, 6, 15, 14, 30);

        var (date, time) = localDt;

        date.Year.Should().Be(2024);
        date.Month.Should().Be(6);
        date.Day.Should().Be(15);
        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
    }

    #endregion

    #region Operators

    [Fact]
    public void Equality_WithSameValue_ReturnsTrue()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 14, 30);
        var dt2 = new LocalDateTime(2024, 6, 15, 14, 30);

        (dt1 == dt2).Should().BeTrue();
        (dt1 != dt2).Should().BeFalse();
    }

    [Fact]
    public void Equality_WithDifferentValue_ReturnsFalse()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 14, 30);
        var dt2 = new LocalDateTime(2024, 6, 15, 15, 30);

        (dt1 == dt2).Should().BeFalse();
        (dt1 != dt2).Should().BeTrue();
    }

    [Fact]
    public void Comparison_WhenEarlier_ReturnsNegative()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 14, 30);
        var dt2 = new LocalDateTime(2024, 6, 15, 15, 30);

        (dt1 < dt2).Should().BeTrue();
        (dt1 <= dt2).Should().BeTrue();
        (dt1 > dt2).Should().BeFalse();
        (dt1 >= dt2).Should().BeFalse();
    }

    [Fact]
    public void AdditionOperator_AddsDuration()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);
        var duration = Duration.FromHours(2);

        var result = dateTime + duration;

        result.Hour.Should().Be(16);
    }

    [Fact]
    public void SubtractionOperator_SubtractsDuration()
    {
        var dateTime = new LocalDateTime(2024, 6, 15, 14, 30);
        var duration = Duration.FromHours(2);

        var result = dateTime - duration;

        result.Hour.Should().Be(12);
    }

    [Fact]
    public void SubtractionOperator_BetweenDateTimes_ReturnsDuration()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 14, 30);
        var dt2 = new LocalDateTime(2024, 6, 15, 10, 0);

        var duration = dt1 - dt2;

        duration.TotalHours.Should().Be(4.5);
    }

    #endregion

    #region Equality & Comparison

    [Fact]
    public void Equals_WithSameValue_ReturnsTrue()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 14, 30);
        var dt2 = new LocalDateTime(2024, 6, 15, 14, 30);

        dt1.Equals(dt2).Should().BeTrue();
        dt1.Equals((object)dt2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithNull_ReturnsFalse()
    {
        var dt = new LocalDateTime(2024, 6, 15, 14, 30);

        dt.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameValues_SameHash()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 14, 30);
        var dt2 = new LocalDateTime(2024, 6, 15, 14, 30);

        dt1.GetHashCode().Should().Be(dt2.GetHashCode());
    }

    [Fact]
    public void CompareTo_WithLaterDateTime_ReturnsNegative()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 14, 30);
        var dt2 = new LocalDateTime(2024, 6, 15, 15, 30);

        dt1.CompareTo(dt2).Should().BeNegative();
    }

    [Fact]
    public void CompareTo_WithEarlierDateTime_ReturnsPositive()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 15, 30);
        var dt2 = new LocalDateTime(2024, 6, 15, 14, 30);

        dt1.CompareTo(dt2).Should().BePositive();
    }

    [Fact]
    public void CompareTo_WithSameDateTime_ReturnsZero()
    {
        var dt1 = new LocalDateTime(2024, 6, 15, 14, 30);
        var dt2 = new LocalDateTime(2024, 6, 15, 14, 30);

        dt1.CompareTo(dt2).Should().Be(0);
    }

    [Fact]
    public void CompareTo_WithInvalidType_ThrowsArgumentException()
    {
        var dt = new LocalDateTime(2024, 6, 15, 14, 30);

        var act = () => dt.CompareTo("not a datetime");

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Parsing & Formatting

    [Fact]
    public void ToString_ReturnsIso8601Format()
    {
        var dt = new LocalDateTime(2024, 6, 15, 14, 30, 45);

        dt.ToString().Should().Be("2024-06-15T14:30:45");
    }

    [Fact]
    public void ToString_WithFormat_ReturnsFormattedString()
    {
        var dt = new LocalDateTime(2024, 6, 15, 14, 30, 45);

        dt.ToString("yyyy-MM-dd").Should().Be("2024-06-15");
        dt.ToString("HH:mm").Should().Be("14:30");
    }

    [Fact]
    public void Parse_WithValidIso8601_ReturnsLocalDateTime()
    {
        var dt = LocalDateTime.Parse("2024-06-15T14:30:45");

        dt.Year.Should().Be(2024);
        dt.Month.Should().Be(6);
        dt.Day.Should().Be(15);
        dt.Hour.Should().Be(14);
        dt.Minute.Should().Be(30);
        dt.Second.Should().Be(45);
    }

    [Fact]
    public void Parse_WithShortFormat_ReturnsLocalDateTime()
    {
        var dt = LocalDateTime.Parse("2024-06-15T14:30");

        dt.Hour.Should().Be(14);
        dt.Minute.Should().Be(30);
        dt.Second.Should().Be(0);
    }

    [Fact]
    public void Parse_WithSpaceFormat_ReturnsLocalDateTime()
    {
        var dt = LocalDateTime.Parse("2024-06-15 14:30:45");

        dt.Hour.Should().Be(14);
        dt.Minute.Should().Be(30);
        dt.Second.Should().Be(45);
    }

    [Fact]
    public void Parse_StripsTimezoneInfo()
    {
        // LocalDateTime ignores timezone suffix
        var dt = LocalDateTime.Parse("2024-06-15T14:30:45Z");

        dt.Hour.Should().Be(14);
        dt.Minute.Should().Be(30);
        dt.Second.Should().Be(45);
    }

    [Fact]
    public void Parse_WithInvalidFormat_ThrowsFormatException()
    {
        var act = () => LocalDateTime.Parse("invalid");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_WithValidFormat_ReturnsTrue()
    {
        var success = LocalDateTime.TryParse("2024-06-15T14:30:45", out var result);

        success.Should().BeTrue();
        result.Hour.Should().Be(14);
    }

    [Fact]
    public void TryParse_WithInvalidFormat_ReturnsFalse()
    {
        var success = LocalDateTime.TryParse("invalid", out var result);

        success.Should().BeFalse();
        result.Should().Be(default);
    }

    [Fact]
    public void TryParse_WithNull_ReturnsFalse()
    {
        var success = LocalDateTime.TryParse(null, out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_WithMilliseconds_ReturnsTrue()
    {
        var success = LocalDateTime.TryParse("2024-06-15T14:30:45.123", out var result);

        success.Should().BeTrue();
        result.Second.Should().Be(45);
    }

    #endregion
}