using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Tests for daylight-saving-time edge cases (spring-forward gaps and fall-back overlaps)
///     handled by <see cref="ZonedDateTime.FromLocal" /> via the DST policies.
/// </summary>
/// <remarks>
///     Timezone is pinned explicitly (US Pacific) so the tests are deterministic regardless of
///     the machine's local timezone. US Pacific 2024 transitions:
///     spring-forward 2024-03-10 02:00→03:00, fall-back 2024-11-03 02:00→01:00.
/// </remarks>
public class DstTransitionTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

    #region NonExistentTimePolicy

    [Fact]
    public void FromLocal_NonExistentTime_ShiftForward_LandsAtStartOfDstGap()
    {
        // 02:30 on the spring-forward day does not exist; the gap is [02:00, 03:00).
        var localDt = new DateTime(2024, 3, 10, 2, 30, 0);

        var zdt = ZonedDateTime.FromLocal(localDt, Pacific, NonExistentTimePolicy.ShiftForward);

        // ShiftForward skips to the end of the gap: 03:00 daylight time (UTC-7).
        zdt.Hour.Should().Be(3);
        zdt.Minute.Should().Be(0);
        zdt.Offset.Should().Be(TimeSpan.FromHours(-7));
    }

    [Fact]
    public void FromLocal_ValidTimeAfterGap_ShiftForward_IsUnchanged()
    {
        // A time after the gap must be preserved exactly.
        var localDt = new DateTime(2024, 3, 10, 4, 15, 0);

        var zdt = ZonedDateTime.FromLocal(localDt, Pacific, NonExistentTimePolicy.ShiftForward);

        zdt.Hour.Should().Be(4);
        zdt.Minute.Should().Be(15);
        zdt.Offset.Should().Be(TimeSpan.FromHours(-7));
    }

    [Fact]
    public void FromLocal_NonExistentTime_ThrowException_PopulatesExceptionDetails()
    {
        var localDt = new DateTime(2024, 3, 10, 2, 30, 0);

        var act = () => ZonedDateTime.FromLocal(localDt, Pacific, NonExistentTimePolicy.ThrowException);

        var ex = act.Should().Throw<NonExistentTimeException>().Which;
        ex.LocalTime.Should().Be(localDt);
        ex.TimeZone.Should().Be(Pacific);
    }

    #endregion

    #region AmbiguousTimePolicy

    [Fact]
    public void FromLocal_AmbiguousTime_UseStandardTime_UsesSmallerOffset()
    {
        // 01:30 on the fall-back day occurs twice; standard time is UTC-8 (the later occurrence).
        var localDt = new DateTime(2024, 11, 3, 1, 30, 0);

        var zdt = ZonedDateTime.FromLocal(localDt, Pacific, ambiguousPolicy: AmbiguousTimePolicy.UseStandardTime);

        zdt.Hour.Should().Be(1);
        zdt.Minute.Should().Be(30);
        zdt.Offset.Should().Be(TimeSpan.FromHours(-8));
    }

    [Fact]
    public void FromLocal_AmbiguousTime_UseDaylightTime_UsesLargerOffset()
    {
        // The same wall-clock time interpreted as daylight time is UTC-7 (the earlier occurrence).
        var localDt = new DateTime(2024, 11, 3, 1, 30, 0);

        var zdt = ZonedDateTime.FromLocal(localDt, Pacific, ambiguousPolicy: AmbiguousTimePolicy.UseDaylightTime);

        zdt.Hour.Should().Be(1);
        zdt.Minute.Should().Be(30);
        zdt.Offset.Should().Be(TimeSpan.FromHours(-7));
    }

    [Fact]
    public void FromLocal_AmbiguousTime_DaylightInstantIsOneHourBeforeStandard()
    {
        // The daylight interpretation is the earlier instant; standard is one hour later in UTC.
        var localDt = new DateTime(2024, 11, 3, 1, 30, 0);

        var daylight = ZonedDateTime.FromLocal(localDt, Pacific, ambiguousPolicy: AmbiguousTimePolicy.UseDaylightTime);
        var standard = ZonedDateTime.FromLocal(localDt, Pacific, ambiguousPolicy: AmbiguousTimePolicy.UseStandardTime);

        (standard.UtcDateTime - daylight.UtcDateTime).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void FromLocal_AmbiguousTime_ThrowException_PopulatesExceptionDetails()
    {
        var localDt = new DateTime(2024, 11, 3, 1, 30, 0);

        var act = () => ZonedDateTime.FromLocal(localDt, Pacific, ambiguousPolicy: AmbiguousTimePolicy.ThrowException);

        var ex = act.Should().Throw<AmbiguousTimeException>().Which;
        ex.LocalTime.Should().Be(localDt);
        ex.TimeZone.Should().Be(Pacific);
    }

    #endregion

    #region FromLocalStrict

    [Fact]
    public void FromLocalStrict_NonExistentTime_Throws()
    {
        var localDt = new DateTime(2024, 3, 10, 2, 30, 0);

        var act = () => ZonedDateTime.FromLocalStrict(localDt, Pacific);

        act.Should().Throw<NonExistentTimeException>();
    }

    [Fact]
    public void FromLocalStrict_AmbiguousTime_Throws()
    {
        var localDt = new DateTime(2024, 11, 3, 1, 30, 0);

        var act = () => ZonedDateTime.FromLocalStrict(localDt, Pacific);

        act.Should().Throw<AmbiguousTimeException>();
    }

    #endregion
}
