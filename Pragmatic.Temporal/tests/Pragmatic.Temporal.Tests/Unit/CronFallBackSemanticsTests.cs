using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Vixie/Cronos DST fall-back semantics: interval expressions (star-based minute
///     or hour field) fire in BOTH passes of the repeated hour; fixed-time expressions
///     fire once, in the first (daylight) pass.
///     Europe/Rome 2026: fall-back on Sunday 2026-10-25, 03:00+02:00 → 02:00+01:00
///     (wall 02:00-02:59 exists twice). America/New_York 2026: fall-back on Sunday
///     2026-11-01, 02:00-04:00 → 01:00-05:00 (wall 01:00-01:59 exists twice).
/// </summary>
public class CronFallBackSemanticsTests
{
    private static readonly TimeZoneInfo Rome = TimeZoneResolver.GetTimeZone("Europe/Rome");
    private static readonly TimeZoneInfo NewYork = TimeZoneResolver.GetTimeZone("America/New_York");

    [Fact]
    public void GetOccurrences_IntervalExpressionRomeFallBack_FiresInBothOffsets()
    {
        var cron = CronExpression.Parse("*/30 * * * *");
        var from = new DateTimeOffset(2026, 10, 25, 1, 45, 0, TimeSpan.FromHours(2));
        var until = new DateTimeOffset(2026, 10, 25, 3, 15, 0, TimeSpan.FromHours(1));

        var occurrences = cron.GetOccurrences(from, until, Rome).ToList();

        DateTimeOffset[] expectedUtc =
        [
            new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero),  // 02:00 +02:00 (first pass)
            new(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), // 02:30 +02:00 (first pass)
            new(2026, 10, 25, 1, 0, 0, TimeSpan.Zero),  // 02:00 +01:00 (second pass)
            new(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), // 02:30 +01:00 (second pass)
            new(2026, 10, 25, 2, 0, 0, TimeSpan.Zero)   // 03:00 +01:00
        ];

        Assert.Equal(expectedUtc.Select(e => e.UtcDateTime), occurrences.Select(o => o.UtcDateTime));
    }

    [Fact]
    public void GetOccurrences_FixedTimeExpressionRomeFallBack_FiresOnceInDaylightPass()
    {
        var cron = CronExpression.Parse("30 2 * * *");
        var from = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(2));
        var until = new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.FromHours(1));

        var occurrences = cron.GetOccurrences(from, until, Rome).ToList();

        var only = Assert.Single(occurrences);
        // 02:30 +02:00 — the first (daylight) pass.
        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), only.UtcDateTime);
    }

    [Fact]
    public void GetNextOccurrence_FixedTimeFromSecondPass_SkipsToNextDay()
    {
        var cron = CronExpression.Parse("30 2 * * *");
        // 02:10 +01:00 = second pass; the daylight 02:30 (+02:00) already fired.
        var from = new DateTimeOffset(2026, 10, 25, 2, 10, 0, TimeSpan.FromHours(1));

        var next = cron.GetNextOccurrence(from, Rome);

        Assert.NotNull(next);
        // Next day 02:30 +01:00 = 01:30Z on the 26th, not 01:30Z on the 25th.
        Assert.Equal(new DateTime(2026, 10, 26, 1, 30, 0, DateTimeKind.Utc), next.Value.UtcDateTime);
    }

    [Fact]
    public void GetOccurrences_IntervalExpressionNewYorkFallBack_FiresInBothOffsets()
    {
        var cron = CronExpression.Parse("*/30 * * * *");
        var from = new DateTimeOffset(2026, 11, 1, 0, 45, 0, TimeSpan.FromHours(-4));
        var until = new DateTimeOffset(2026, 11, 1, 2, 15, 0, TimeSpan.FromHours(-5));

        var occurrences = cron.GetOccurrences(from, until, NewYork).ToList();

        DateTimeOffset[] expectedUtc =
        [
            new(2026, 11, 1, 5, 0, 0, TimeSpan.Zero),  // 01:00 -04:00 (EDT)
            new(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), // 01:30 -04:00 (EDT)
            new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero),  // 01:00 -05:00 (EST)
            new(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), // 01:30 -05:00 (EST)
            new(2026, 11, 1, 7, 0, 0, TimeSpan.Zero)   // 02:00 -05:00 (EST)
        ];

        Assert.Equal(expectedUtc.Select(e => e.UtcDateTime), occurrences.Select(o => o.UtcDateTime));
    }

    [Fact]
    public void GetNextOccurrence_ChainedThroughFallBack_IsStrictlyMonotone()
    {
        var cron = CronExpression.Parse("*/15 * * * *");
        var current = new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.FromHours(2));

        var previousUtc = current.UtcDateTime;
        for (var i = 0; i < 16; i++)
        {
            var next = cron.GetNextOccurrence(current, Rome);
            Assert.NotNull(next);
            Assert.True(next.Value.UtcDateTime > previousUtc,
                $"Occurrence {next.Value:O} is not after {previousUtc:O}");

            previousUtc = next.Value.UtcDateTime;
            current = next.Value;
        }

        // 16 quarter-hour steps from 01:00+02:00 must cross the whole repeated hour:
        // 01:15..01:45 (3) + 02:00..02:45 ×2 passes (8) + 03:00..03:45 (4) = 15 → the
        // 16th lands at 04:00+01:00.
        Assert.Equal(new DateTime(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc), previousUtc);
    }

    [Fact]
    public void GetOccurrences_HourlyExpressionRomeFallBack_RunsEveryRealHour()
    {
        // "0 * * * *" has a star-based hour → interval: an hourly job runs every real
        // hour, including both 02:00 wall times.
        var cron = CronExpression.Parse("0 * * * *");
        var from = new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.FromHours(2));
        var until = new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.FromHours(1));

        var occurrences = cron.GetOccurrences(from, until, Rome).ToList();

        DateTimeOffset[] expectedUtc =
        [
            new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero), // 02:00 +02:00
            new(2026, 10, 25, 1, 0, 0, TimeSpan.Zero), // 02:00 +01:00
            new(2026, 10, 25, 2, 0, 0, TimeSpan.Zero)  // 03:00 +01:00
        ];

        Assert.Equal(expectedUtc.Select(e => e.UtcDateTime), occurrences.Select(o => o.UtcDateTime));
    }

    [Fact]
    public void GetOccurrences_FixedTimeWithListMinutes_IsNotInterval()
    {
        // "0,30 2 * * *" uses a list, not a star-based field → fixed-time by the
        // Vixie rule: fires only in the daylight pass.
        var cron = CronExpression.Parse("0,30 2 * * *");
        var from = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(2));
        var until = new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.FromHours(1));

        var occurrences = cron.GetOccurrences(from, until, Rome).ToList();

        DateTimeOffset[] expectedUtc =
        [
            new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero), // 02:00 +02:00 only
            new(2026, 10, 25, 0, 30, 0, TimeSpan.Zero) // 02:30 +02:00 only
        ];

        Assert.Equal(expectedUtc.Select(e => e.UtcDateTime), occurrences.Select(o => o.UtcDateTime));
    }
}
