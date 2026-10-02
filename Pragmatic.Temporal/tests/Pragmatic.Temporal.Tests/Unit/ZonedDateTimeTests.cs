using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Testing;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

public class ZonedDateTimeTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    #region Factory Methods

    [Fact]
    public void FromUtc_CreatesCorrectInstance()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);

        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        zdt.UtcDateTime.Should().Be(utc);
        zdt.Zone.Should().Be(Pacific);
    }

    [Fact]
    public void FromUtc_WithTimezoneId_CreatesCorrectInstance()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);

        var zdt = ZonedDateTime.FromUtc(utc, "America/Los_Angeles");

        zdt.UtcDateTime.Should().Be(utc);
    }

    [Fact]
    public void FromLocal_CreatesCorrectInstance()
    {
        var localDt = new DateTime(2024, 6, 15, 10, 30, 0);

        var zdt = ZonedDateTime.FromLocal(localDt, Pacific);

        zdt.Hour.Should().Be(10);
        zdt.Minute.Should().Be(30);
    }

    [Fact]
    public void FromLocal_WithNonExistentTime_ShiftsForward()
    {
        // March 10, 2024 at 2:30 AM doesn't exist in Pacific (spring forward)
        var localDt = new DateTime(2024, 3, 10, 2, 30, 0);

        var zdt = ZonedDateTime.FromLocal(localDt, Pacific);

        // Should shift to 3:00 AM or later
        zdt.Hour.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public void FromLocal_WithNonExistentTime_ThrowsWhenConfigured()
    {
        // March 10, 2024 at 2:30 AM doesn't exist in Pacific (spring forward)
        var localDt = new DateTime(2024, 3, 10, 2, 30, 0);

        var act = () => ZonedDateTime.FromLocal(localDt, Pacific, NonExistentTimePolicy.ThrowException);

        act.Should().Throw<NonExistentTimeException>();
    }

    [Fact]
    public void FromLocal_WithAmbiguousTime_UsesStandardTimeByDefault()
    {
        // November 3, 2024 at 1:30 AM is ambiguous in Pacific (fall back)
        var localDt = new DateTime(2024, 11, 3, 1, 30, 0);

        var zdt = ZonedDateTime.FromLocal(localDt, Pacific, ambiguousPolicy: AmbiguousTimePolicy.UseStandardTime);

        zdt.Hour.Should().Be(1);
        zdt.Minute.Should().Be(30);
    }

    [Fact]
    public void FromLocal_WithAmbiguousTime_ThrowsWhenConfigured()
    {
        // November 3, 2024 at 1:30 AM is ambiguous in Pacific (fall back)
        var localDt = new DateTime(2024, 11, 3, 1, 30, 0);

        var act = () => ZonedDateTime.FromLocal(localDt, Pacific, ambiguousPolicy: AmbiguousTimePolicy.ThrowException);

        act.Should().Throw<AmbiguousTimeException>();
    }

    [Fact]
    public void FromLocalStrict_WithValidTime_CreatesInstance()
    {
        var localDt = new DateTime(2024, 6, 15, 10, 30, 0);

        var zdt = ZonedDateTime.FromLocalStrict(localDt, Pacific);

        zdt.Hour.Should().Be(10);
    }

    [Fact]
    public void Now_ReturnsCurrentTimeInZone()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero));

        var zdt = ZonedDateTime.Now(Pacific, clock);

        zdt.Zone.Should().Be(Pacific);
    }

    [Fact]
    public void Now_WithTimezoneId_ReturnsCurrentTimeInZone()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero));

        var zdt = ZonedDateTime.Now("America/Los_Angeles", clock);

        zdt.ZoneId.Should().Contain("Los_Angeles");
    }

    #endregion

    #region Properties

    [Fact]
    public void Properties_ReturnLocalValues()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 45, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        // Pacific is UTC-7 in summer
        zdt.Hour.Should().Be(14); // 21 - 7
        zdt.Minute.Should().Be(30);
        zdt.Second.Should().Be(45);
        zdt.Year.Should().Be(2024);
        zdt.Month.Should().Be(6);
        zdt.Day.Should().Be(15);
    }

    [Fact]
    public void Date_ReturnsLocalDate()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        zdt.Date.Year.Should().Be(2024);
        zdt.Date.Month.Should().Be(6);
        zdt.Date.Day.Should().Be(15);
    }

    [Fact]
    public void Time_ReturnsLocalTime()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 45, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        zdt.Time.Hour.Should().Be(14); // 21 - 7
        zdt.Time.Minute.Should().Be(30);
    }

    [Fact]
    public void ZoneId_ReturnsIanaId()
    {
        var zdt = ZonedDateTime.FromUtc(DateTimeOffset.UtcNow, Pacific);

        zdt.ZoneId.Should().NotBeEmpty();
    }

    [Fact]
    public void Offset_ReturnsCorrectOffset()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        // Pacific is UTC-7 in summer
        zdt.Offset.Should().Be(TimeSpan.FromHours(-7));
    }

    [Fact]
    public void IsDaylightSavingTime_ReturnsTrueInSummer()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        zdt.IsDaylightSavingTime.Should().BeTrue();
    }

    [Fact]
    public void IsDaylightSavingTime_ReturnsFalseInWinter()
    {
        var utc = new DateTimeOffset(2024, 1, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        zdt.IsDaylightSavingTime.Should().BeFalse();
    }

    #endregion

    #region Conversion

    [Fact]
    public void InZone_SameInstantDifferentLocalTime()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdtPacific = ZonedDateTime.FromUtc(utc, Pacific);

        var zdtEastern = zdtPacific.InZone(Eastern);

        // Same instant, different local time
        zdtPacific.UtcDateTime.Should().Be(zdtEastern.UtcDateTime);
        zdtEastern.Hour.Should().Be(zdtPacific.Hour + 3); // Eastern is 3 hours ahead
    }

    [Fact]
    public void InZone_WithTimezoneId_Works()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var zdtEastern = zdt.InZone("America/New_York");

        zdtEastern.ZoneId.Should().Contain("New_York");
    }

    [Fact]
    public void ToUtc_ReturnsUtcInstant()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        zdt.ToUtc().Should().Be(utc);
    }

    [Fact]
    public void ToDateTimeOffset_ReturnsLocalWithOffset()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var dto = zdt.ToDateTimeOffset();

        dto.Hour.Should().Be(14); // Local time
        dto.Offset.Should().Be(TimeSpan.FromHours(-7)); // Pacific summer offset
    }

    [Fact]
    public void Deconstruct_ThreeParams_ReturnsComponents()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var (date, time, zone) = zdt;

        date.Year.Should().Be(2024);
        time.Hour.Should().Be(14);
        zone.Should().Be(Pacific);
    }

    [Fact]
    public void Deconstruct_TwoParams_ReturnsUtcAndZone()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        zdt.Deconstruct(out var resultUtc, out var resultZone);

        resultUtc.Should().Be(utc);
        resultZone.Should().Be(Pacific);
    }

    #endregion

    #region Arithmetic

    [Fact]
    public void AddDays_ReturnsCorrectResult()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var result = zdt.AddDays(10);

        result.Day.Should().Be(25);
        result.Hour.Should().Be(zdt.Hour); // Same local time
    }

    [Fact]
    public void AddMonths_ReturnsCorrectResult()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var result = zdt.AddMonths(3);

        result.Month.Should().Be(9);
    }

    [Fact]
    public void AddYears_ReturnsCorrectResult()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var result = zdt.AddYears(2);

        result.Year.Should().Be(2026);
    }

    [Fact]
    public void AddHours_ReturnsCorrectResult()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var result = zdt.AddHours(3);

        result.Hour.Should().Be(17); // 14 + 3
    }

    [Fact]
    public void AddMinutes_ReturnsCorrectResult()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var result = zdt.AddMinutes(45);

        result.Hour.Should().Be(15);
        result.Minute.Should().Be(15);
    }

    [Fact]
    public void Add_Duration_ReturnsCorrectResult()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);
        var duration = Duration.FromHours(2);

        var result = zdt.Add(duration);

        result.UtcDateTime.Should().Be(utc.AddHours(2));
    }

    [Fact]
    public void Add_TimeSpan_ReturnsCorrectResult()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var result = zdt.Add(TimeSpan.FromHours(2));

        result.UtcDateTime.Should().Be(utc.AddHours(2));
    }

    [Fact]
    public void DurationUntil_ReturnsCorrectDuration()
    {
        var utc1 = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var utc2 = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc1, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc2, Pacific);

        var duration = zdt1.DurationUntil(zdt2);

        duration.TotalHours.Should().Be(4.5);
    }

    #endregion

    #region Operators

    [Fact]
    public void Equality_SameInstant_ReturnsTrue()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc, Pacific);

        (zdt1 == zdt2).Should().BeTrue();
        (zdt1 != zdt2).Should().BeFalse();
    }

    [Fact]
    public void Equality_SameInstantDifferentZone_ReturnsTrue()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc, Eastern);

        // Same instant, different zones - still equal
        (zdt1 == zdt2).Should().BeTrue();
    }

    [Fact]
    public void EqualsExact_SameInstantDifferentZone_ReturnsFalse()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc, Eastern);

        zdt1.EqualsExact(zdt2).Should().BeFalse();
    }

    [Fact]
    public void EqualsExact_SameInstantSameZone_ReturnsTrue()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc, Pacific);

        zdt1.EqualsExact(zdt2).Should().BeTrue();
    }

    [Fact]
    public void Comparison_EarlierInstant_ReturnsNegative()
    {
        var utc1 = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var utc2 = new DateTimeOffset(2024, 6, 15, 15, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc1, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc2, Pacific);

        (zdt1 < zdt2).Should().BeTrue();
        (zdt1 <= zdt2).Should().BeTrue();
        (zdt1 > zdt2).Should().BeFalse();
        (zdt1 >= zdt2).Should().BeFalse();
    }

    [Fact]
    public void AdditionOperator_AddsDuration()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);
        var duration = Duration.FromHours(2);

        var result = zdt + duration;

        result.UtcDateTime.Should().Be(utc.AddHours(2));
    }

    [Fact]
    public void SubtractionOperator_SubtractsDuration()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);
        var duration = Duration.FromHours(2);

        var result = zdt - duration;

        result.UtcDateTime.Should().Be(utc.AddHours(-2));
    }

    [Fact]
    public void SubtractionOperator_BetweenZonedDateTimes_ReturnsDuration()
    {
        var utc1 = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var utc2 = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc1, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc2, Pacific);

        var duration = zdt1 - zdt2;

        duration.TotalHours.Should().Be(4.5);
    }

    #endregion

    #region Equality & Comparison

    [Fact]
    public void Equals_WithSameInstance_ReturnsTrue()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc, Pacific);

        zdt1.Equals(zdt2).Should().BeTrue();
        zdt1.Equals((object)zdt2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithNull_ReturnsFalse()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        zdt.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_SameInstant_SameHash()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc, Pacific);

        zdt1.GetHashCode().Should().Be(zdt2.GetHashCode());
    }

    [Fact]
    public void CompareTo_WithLaterInstant_ReturnsNegative()
    {
        var utc1 = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var utc2 = new DateTimeOffset(2024, 6, 15, 15, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc1, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc2, Pacific);

        zdt1.CompareTo(zdt2).Should().BeNegative();
    }

    [Fact]
    public void CompareTo_WithEarlierInstant_ReturnsPositive()
    {
        var utc1 = new DateTimeOffset(2024, 6, 15, 15, 30, 0, TimeSpan.Zero);
        var utc2 = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc1, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc2, Pacific);

        zdt1.CompareTo(zdt2).Should().BePositive();
    }

    [Fact]
    public void CompareTo_WithSameInstant_ReturnsZero()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt1 = ZonedDateTime.FromUtc(utc, Pacific);
        var zdt2 = ZonedDateTime.FromUtc(utc, Pacific);

        zdt1.CompareTo(zdt2).Should().Be(0);
    }

    [Fact]
    public void CompareTo_WithInvalidType_ThrowsArgumentException()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 0, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var act = () => zdt.CompareTo("not a zoned datetime");

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Parsing & Formatting

    [Fact]
    public void ToString_ReturnsIso8601WithZone()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 45, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var str = zdt.ToString();

        str.Should().Contain("2024-06-15");
        str.Should().Contain("14:30:45"); // Local time
        str.Should().Contain("["); // Zone annotation
    }

    [Fact]
    public void ToString_WithFormat_ReturnsFormattedString()
    {
        var utc = new DateTimeOffset(2024, 6, 15, 21, 30, 45, TimeSpan.Zero);
        var zdt = ZonedDateTime.FromUtc(utc, Pacific);

        var str = zdt.ToString("yyyy-MM-dd");

        str.Should().Be("2024-06-15");
    }

    [Fact]
    public void Parse_WithZoneAnnotation_ReturnsZonedDateTime()
    {
        var str = "2024-06-15T14:30:45-07:00[America/Los_Angeles]";

        var zdt = ZonedDateTime.Parse(str);

        zdt.Hour.Should().Be(14);
        zdt.Minute.Should().Be(30);
        zdt.ZoneId.Should().Contain("Los_Angeles");
    }

    [Fact]
    public void Parse_WithUtcFormat_ReturnsUtcZone()
    {
        var str = "2024-06-15T21:30:45Z";

        var zdt = ZonedDateTime.Parse(str);

        zdt.Zone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void Parse_WithInvalidFormat_ThrowsFormatException()
    {
        var act = () => ZonedDateTime.Parse("invalid");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_WithValidFormat_ReturnsTrue()
    {
        var str = "2024-06-15T14:30:45-07:00[America/Los_Angeles]";

        var success = ZonedDateTime.TryParse(str, out var result);

        success.Should().BeTrue();
        result.Hour.Should().Be(14);
    }

    [Fact]
    public void TryParse_WithInvalidFormat_ReturnsFalse()
    {
        var success = ZonedDateTime.TryParse("invalid", out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_WithNull_ReturnsFalse()
    {
        var success = ZonedDateTime.TryParse(null, out _);

        success.Should().BeFalse();
    }

    #endregion
}