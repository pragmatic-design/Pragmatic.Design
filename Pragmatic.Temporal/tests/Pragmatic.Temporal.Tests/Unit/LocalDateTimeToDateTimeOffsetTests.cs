using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Regression tests for finding #22: ToDateTimeOffset must be DST-safe,
///     consistent with InZone (gap → shift forward, ambiguity → policy-driven).
/// </summary>
public class LocalDateTimeToDateTimeOffsetTests
{
    private static readonly TimeZoneInfo Rome = TimeZoneResolver.GetTimeZone("Europe/Rome");

    [Fact]
    public void ToDateTimeOffset_NormalTime_UsesZoneOffset()
    {
        var winter = new LocalDateTime(2026, 1, 15, 10, 0);

        var result = winter.ToDateTimeOffset(Rome);

        result.Offset.Should().Be(TimeSpan.FromHours(1));
        result.DateTime.Should().Be(new DateTime(2026, 1, 15, 10, 0, 0));
    }

    [Fact]
    public void ToDateTimeOffset_InDstGap_ShiftsForwardByDefault()
    {
        // 2026-03-29 02:30 does not exist in Europe/Rome (spring forward 02:00 → 03:00).
        var gap = new LocalDateTime(2026, 3, 29, 2, 30);

        var result = gap.ToDateTimeOffset(Rome);

        // Same instant InZone would produce: shifted to the end of the gap.
        var viaInZone = gap.InZone(Rome).ToDateTimeOffset();
        result.Should().Be(viaInZone);
        result.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void ToDateTimeOffset_InDstGap_ThrowPolicy_Throws()
    {
        var gap = new LocalDateTime(2026, 3, 29, 2, 30);

        var act = () => gap.ToDateTimeOffset(Rome, NonExistentTimePolicy.ThrowException,
            AmbiguousTimePolicy.UseStandardTime);

        act.Should().Throw<NonExistentTimeException>();
    }

    [Fact]
    public void ToDateTimeOffset_AmbiguousTime_UsesStandardTimeByDefault()
    {
        // 2026-10-25 02:30 occurs twice in Europe/Rome (fall back 03:00 → 02:00).
        var ambiguous = new LocalDateTime(2026, 10, 25, 2, 30);

        var result = ambiguous.ToDateTimeOffset(Rome);

        // Standard time = smaller offset (+01:00).
        result.Offset.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void ToDateTimeOffset_AmbiguousTime_DaylightPolicy_UsesDaylightOffset()
    {
        var ambiguous = new LocalDateTime(2026, 10, 25, 2, 30);

        var result = ambiguous.ToDateTimeOffset(Rome, NonExistentTimePolicy.ShiftForward,
            AmbiguousTimePolicy.UseDaylightTime);

        result.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void ToDateTimeOffset_AmbiguousTime_ThrowPolicy_Throws()
    {
        var ambiguous = new LocalDateTime(2026, 10, 25, 2, 30);

        var act = () => ambiguous.ToDateTimeOffset(Rome, NonExistentTimePolicy.ShiftForward,
            AmbiguousTimePolicy.ThrowException);

        act.Should().Throw<AmbiguousTimeException>();
    }
}
