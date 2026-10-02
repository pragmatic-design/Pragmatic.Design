using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

public class LocalTimeTests
{
    #region Constructor

    [Fact]
    public void Constructor_WithHourAndMinute_CreatesInstance()
    {
        var time = new LocalTime(14, 30);

        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
        time.Second.Should().Be(0);
        time.Millisecond.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithHourMinuteSecond_CreatesInstance()
    {
        var time = new LocalTime(14, 30, 45);

        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
        time.Second.Should().Be(45);
        time.Millisecond.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithHourMinuteSecondMillisecond_CreatesInstance()
    {
        var time = new LocalTime(14, 30, 45, 123);

        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
        time.Second.Should().Be(45);
        time.Millisecond.Should().Be(123);
    }

    [Fact]
    public void Constructor_FromTimeOnly_CreatesInstance()
    {
        var timeOnly = new TimeOnly(14, 30, 45);
        var time = new LocalTime(timeOnly);

        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
        time.Second.Should().Be(45);
    }

    #endregion

    #region Common Times

    [Fact]
    public void Midnight_IsZeroTime()
    {
        LocalTime.Midnight.Hour.Should().Be(0);
        LocalTime.Midnight.Minute.Should().Be(0);
        LocalTime.Midnight.Second.Should().Be(0);
    }

    [Fact]
    public void Noon_IsTwelveOClock()
    {
        LocalTime.Noon.Hour.Should().Be(12);
        LocalTime.Noon.Minute.Should().Be(0);
        LocalTime.Noon.Second.Should().Be(0);
    }

    [Fact]
    public void MinValue_IsMidnight()
    {
        LocalTime.MinValue.Should().Be(LocalTime.Midnight);
    }

    [Fact]
    public void MaxValue_IsEndOfDay()
    {
        LocalTime.MaxValue.Hour.Should().Be(23);
        LocalTime.MaxValue.Minute.Should().Be(59);
        LocalTime.MaxValue.Second.Should().Be(59);
    }

    #endregion

    #region Factory Methods

    [Fact]
    public void FromDateTime_ExtractsTimeComponent()
    {
        var dateTime = new DateTime(2024, 6, 15, 14, 30, 45);

        var time = LocalTime.FromDateTime(dateTime);

        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
        time.Second.Should().Be(45);
    }

    [Fact]
    public void FromDateTimeOffset_ExtractsTimeComponent()
    {
        var dateTimeOffset = new DateTimeOffset(2024, 6, 15, 14, 30, 45, TimeSpan.FromHours(2));

        var time = LocalTime.FromDateTimeOffset(dateTimeOffset);

        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
        time.Second.Should().Be(45);
    }

    [Fact]
    public void FromTicks_CreatesCorrectTime()
    {
        var expected = new LocalTime(14, 30, 45);
        var ticks = expected.Ticks;

        var time = LocalTime.FromTicks(ticks);

        time.Should().Be(expected);
    }

    [Fact]
    public void FromTimeSpan_CreatesCorrectTime()
    {
        var timeSpan = new TimeSpan(14, 30, 45);

        var time = LocalTime.FromTimeSpan(timeSpan);

        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
        time.Second.Should().Be(45);
    }

    #endregion

    #region Arithmetic

    [Fact]
    public void Add_WithTimeSpan_ReturnsCorrectTime()
    {
        var time = new LocalTime(14, 30);

        var result = time.Add(TimeSpan.FromHours(2));

        result.Hour.Should().Be(16);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void Add_WithDuration_ReturnsCorrectTime()
    {
        var time = new LocalTime(14, 30);
        var duration = Duration.FromHours(2);

        var result = time.Add(duration);

        result.Hour.Should().Be(16);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void Add_WrapsAroundMidnight()
    {
        var time = new LocalTime(23, 30);

        var result = time.Add(TimeSpan.FromHours(2));

        result.Hour.Should().Be(1);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void AddHours_ReturnsCorrectTime()
    {
        var time = new LocalTime(14, 30);

        var result = time.AddHours(3);

        result.Hour.Should().Be(17);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void AddMinutes_ReturnsCorrectTime()
    {
        var time = new LocalTime(14, 30);

        var result = time.AddMinutes(45);

        result.Hour.Should().Be(15);
        result.Minute.Should().Be(15);
    }

    [Fact]
    public void DurationUntil_WhenOtherIsLater_ReturnsPositiveDuration()
    {
        var time1 = new LocalTime(10, 0);
        var time2 = new LocalTime(14, 30);

        var duration = time1.DurationUntil(time2);

        duration.Should().Be(TimeSpan.FromHours(4.5));
    }

    [Fact]
    public void DurationUntil_WhenOtherIsEarlier_WrapsAround()
    {
        var time1 = new LocalTime(23, 0);
        var time2 = new LocalTime(1, 0);

        var duration = time1.DurationUntil(time2);

        duration.Should().Be(TimeSpan.FromHours(2));
    }

    #endregion

    #region Checks

    [Fact]
    public void IsBetween_WhenInRange_ReturnsTrue()
    {
        var time = new LocalTime(14, 30);
        var start = new LocalTime(9, 0);
        var end = new LocalTime(17, 0);

        time.IsBetween(start, end).Should().BeTrue();
    }

    [Fact]
    public void IsBetween_WhenOutOfRange_ReturnsFalse()
    {
        var time = new LocalTime(8, 0);
        var start = new LocalTime(9, 0);
        var end = new LocalTime(17, 0);

        time.IsBetween(start, end).Should().BeFalse();
    }

    [Fact]
    public void IsMorning_BeforeNoon_ReturnsTrue()
    {
        var time = new LocalTime(9, 30);

        time.IsMorning.Should().BeTrue();
        time.IsAfternoon.Should().BeFalse();
        time.IsEvening.Should().BeFalse();
    }

    [Fact]
    public void IsAfternoon_Between12And18_ReturnsTrue()
    {
        var time = new LocalTime(14, 30);

        time.IsMorning.Should().BeFalse();
        time.IsAfternoon.Should().BeTrue();
        time.IsEvening.Should().BeFalse();
    }

    [Fact]
    public void IsEvening_After18_ReturnsTrue()
    {
        var time = new LocalTime(20, 0);

        time.IsMorning.Should().BeFalse();
        time.IsAfternoon.Should().BeFalse();
        time.IsEvening.Should().BeTrue();
    }

    [Fact]
    public void IsMorning_AtNoon_ReturnsFalse()
    {
        LocalTime.Noon.IsMorning.Should().BeFalse();
    }

    [Fact]
    public void IsAfternoon_AtExactly18_ReturnsFalse()
    {
        var time = new LocalTime(18, 0);

        time.IsAfternoon.Should().BeFalse();
        time.IsEvening.Should().BeTrue();
    }

    #endregion

    #region Conversion

    [Fact]
    public void ToTimeOnly_ReturnsCorrectValue()
    {
        var time = new LocalTime(14, 30, 45);

        var timeOnly = time.ToTimeOnly();

        timeOnly.Hour.Should().Be(14);
        timeOnly.Minute.Should().Be(30);
        timeOnly.Second.Should().Be(45);
    }

    [Fact]
    public void ToTimeSpan_ReturnsCorrectValue()
    {
        var time = new LocalTime(14, 30, 45);

        var timeSpan = time.ToTimeSpan();

        timeSpan.Should().Be(new TimeSpan(14, 30, 45));
    }

    [Fact]
    public void On_WithLocalDate_ReturnsLocalDateTime()
    {
        var time = new LocalTime(14, 30);
        var date = new LocalDate(2024, 6, 15);

        var result = time.On(date);

        result.Year.Should().Be(2024);
        result.Month.Should().Be(6);
        result.Day.Should().Be(15);
        result.Hour.Should().Be(14);
        result.Minute.Should().Be(30);
    }

    #endregion

    #region Operators

    [Fact]
    public void Equality_WithSameTime_ReturnsTrue()
    {
        var time1 = new LocalTime(14, 30);
        var time2 = new LocalTime(14, 30);

        (time1 == time2).Should().BeTrue();
        (time1 != time2).Should().BeFalse();
    }

    [Fact]
    public void Equality_WithDifferentTime_ReturnsFalse()
    {
        var time1 = new LocalTime(14, 30);
        var time2 = new LocalTime(15, 30);

        (time1 == time2).Should().BeFalse();
        (time1 != time2).Should().BeTrue();
    }

    [Fact]
    public void Comparison_WhenEarlier_ReturnsNegative()
    {
        var time1 = new LocalTime(14, 30);
        var time2 = new LocalTime(15, 30);

        (time1 < time2).Should().BeTrue();
        (time1 <= time2).Should().BeTrue();
        (time1 > time2).Should().BeFalse();
        (time1 >= time2).Should().BeFalse();
    }

    [Fact]
    public void AdditionOperator_AddsTimeSpan()
    {
        var time = new LocalTime(14, 30);

        var result = time + TimeSpan.FromHours(1);

        result.Hour.Should().Be(15);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void SubtractionOperator_SubtractsTimeSpan()
    {
        var time = new LocalTime(14, 30);

        var result = time - TimeSpan.FromHours(1);

        result.Hour.Should().Be(13);
        result.Minute.Should().Be(30);
    }

    [Fact]
    public void ImplicitConversion_ToTimeOnly_Works()
    {
        var localTime = new LocalTime(14, 30);

        TimeOnly timeOnly = localTime;

        timeOnly.Hour.Should().Be(14);
        timeOnly.Minute.Should().Be(30);
    }

    [Fact]
    public void ImplicitConversion_FromTimeOnly_Works()
    {
        var timeOnly = new TimeOnly(14, 30);

        LocalTime localTime = timeOnly;

        localTime.Hour.Should().Be(14);
        localTime.Minute.Should().Be(30);
    }

    #endregion

    #region Equality & Comparison

    [Fact]
    public void Equals_WithSameValue_ReturnsTrue()
    {
        var time1 = new LocalTime(14, 30);
        var time2 = new LocalTime(14, 30);

        time1.Equals(time2).Should().BeTrue();
        time1.Equals((object)time2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithNull_ReturnsFalse()
    {
        var time = new LocalTime(14, 30);

        time.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameValues_SameHash()
    {
        var time1 = new LocalTime(14, 30);
        var time2 = new LocalTime(14, 30);

        time1.GetHashCode().Should().Be(time2.GetHashCode());
    }

    [Fact]
    public void CompareTo_WithLaterTime_ReturnsNegative()
    {
        var time1 = new LocalTime(14, 30);
        var time2 = new LocalTime(15, 30);

        time1.CompareTo(time2).Should().BeNegative();
    }

    [Fact]
    public void CompareTo_WithEarlierTime_ReturnsPositive()
    {
        var time1 = new LocalTime(15, 30);
        var time2 = new LocalTime(14, 30);

        time1.CompareTo(time2).Should().BePositive();
    }

    [Fact]
    public void CompareTo_WithSameTime_ReturnsZero()
    {
        var time1 = new LocalTime(14, 30);
        var time2 = new LocalTime(14, 30);

        time1.CompareTo(time2).Should().Be(0);
    }

    [Fact]
    public void CompareTo_WithInvalidType_ThrowsArgumentException()
    {
        var time = new LocalTime(14, 30);

        var act = () => time.CompareTo("not a time");

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Parsing & Formatting

    [Fact]
    public void ToString_ReturnsIso8601Format()
    {
        var time = new LocalTime(14, 30, 45);

        time.ToString().Should().Be("14:30:45");
    }

    [Fact]
    public void ToString_WithFormat_ReturnsFormattedString()
    {
        var time = new LocalTime(14, 30, 45);

        time.ToString("HH:mm").Should().Be("14:30");
        time.ToString("hh:mm tt").Should().Contain("02:30"); // 12-hour format
    }

    [Fact]
    public void Parse_WithValidIso8601_ReturnsLocalTime()
    {
        var time = LocalTime.Parse("14:30:45");

        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
        time.Second.Should().Be(45);
    }

    [Fact]
    public void Parse_WithShortFormat_ReturnsLocalTime()
    {
        var time = LocalTime.Parse("14:30");

        time.Hour.Should().Be(14);
        time.Minute.Should().Be(30);
        time.Second.Should().Be(0);
    }

    [Fact]
    public void Parse_WithInvalidFormat_ThrowsFormatException()
    {
        var act = () => LocalTime.Parse("invalid");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_WithValidFormat_ReturnsTrue()
    {
        var success = LocalTime.TryParse("14:30:45", out var result);

        success.Should().BeTrue();
        result.Hour.Should().Be(14);
        result.Minute.Should().Be(30);
        result.Second.Should().Be(45);
    }

    [Fact]
    public void TryParse_WithInvalidFormat_ReturnsFalse()
    {
        var success = LocalTime.TryParse("invalid", out var result);

        success.Should().BeFalse();
        result.Should().Be(default);
    }

    [Fact]
    public void TryParse_WithNull_ReturnsFalse()
    {
        var success = LocalTime.TryParse(null, out var result);

        success.Should().BeFalse();
        result.Should().Be(default);
    }

    [Fact]
    public void TryParse_WithMilliseconds_ReturnsTrue()
    {
        var success = LocalTime.TryParse("14:30:45.123", out var result);

        success.Should().BeTrue();
        result.Millisecond.Should().Be(123);
    }

    #endregion
}