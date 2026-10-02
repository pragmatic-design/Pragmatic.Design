using Microsoft.EntityFrameworkCore;
using Pragmatic.Temporal.EntityFrameworkCore;
using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>
///     Round-trips every temporal type (and nullable variants, both set and null)
///     through a real Sqlite database using only UsePragmaticTemporal().
/// </summary>
public class ValueConverterRoundTripTests
{
    private static TemporalEntity CreateFullEntity()
    {
        var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");
        return new TemporalEntity
        {
            Date = new LocalDate(2026, 6, 15),
            NullableDate = new LocalDate(2026, 12, 25),
            Time = new LocalTime(9, 30, 15),
            NullableTime = new LocalTime(23, 59, 59),
            WallDateTime = new LocalDateTime(2026, 6, 15, 9, 30, 0),
            NullableWallDateTime = new LocalDateTime(2026, 1, 1, 0, 0, 0),
            Zoned = ZonedDateTime.FromUtc(new DateTimeOffset(2026, 6, 15, 7, 30, 0, TimeSpan.Zero), rome),
            NullableZoned = ZonedDateTime.FromUtc(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero), "Europe/London"),
            Duration = Duration.FromMinutes(90),
            NullableDuration = Duration.FromDays(1.5),
            Period = new Period(1, 2, 3),
            NullablePeriod = Period.FromMonths(6),
            Range = new DateRange(new LocalDate(2026, 1, 1), new LocalDate(2026, 12, 31)),
            NullableRange = DateRange.SingleDay(new LocalDate(2026, 6, 15)),
            Cron = CronExpression.Parse("30 2 * * 1-5"),
            NullableCron = CronExpression.Parse("0 0 1 * *")
        };
    }

    [Fact]
    public void AllTemporalTypes_RoundTrip_ThroughSqlite()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<AutoTemporalContext>(
            b => b.UsePragmaticTemporal());
        using var _ = connection;

        var original = CreateFullEntity();
        int id;
        using (var context = new AutoTemporalContext(options))
        {
            context.Database.EnsureCreated();
            context.Entities.Add(original);
            context.SaveChanges();
            id = original.Id;
        }

        using (var context = new AutoTemporalContext(options))
        {
            var loaded = context.Entities.Single(e => e.Id == id);

            Assert.Equal(original.Date, loaded.Date);
            Assert.Equal(original.NullableDate, loaded.NullableDate);
            Assert.Equal(original.Time, loaded.Time);
            Assert.Equal(original.NullableTime, loaded.NullableTime);
            Assert.Equal(original.WallDateTime, loaded.WallDateTime);
            Assert.Equal(DateTimeKind.Unspecified, loaded.WallDateTime.ToDateTime().Kind);
            Assert.Equal(original.NullableWallDateTime, loaded.NullableWallDateTime);
            Assert.True(original.Zoned.EqualsExact(loaded.Zoned)); // bracket format keeps the zone
            Assert.Equal(original.NullableZoned, loaded.NullableZoned);
            Assert.Equal(original.Duration, loaded.Duration);
            Assert.Equal(original.NullableDuration, loaded.NullableDuration);
            Assert.Equal(original.Period, loaded.Period);
            Assert.Equal(original.NullablePeriod, loaded.NullablePeriod);
            Assert.Equal(original.Range, loaded.Range);
            Assert.Equal(original.NullableRange, loaded.NullableRange);
            Assert.Equal(original.Cron.Expression, loaded.Cron.Expression);
            Assert.Equal(original.NullableCron!.Expression, loaded.NullableCron!.Expression);
        }
    }

    [Fact]
    public void NullableTemporalTypes_RoundTrip_Null()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<AutoTemporalContext>(
            b => b.UsePragmaticTemporal());
        using var _ = connection;

        var entity = CreateFullEntity();
        entity.NullableDate = null;
        entity.NullableTime = null;
        entity.NullableWallDateTime = null;
        entity.NullableZoned = null;
        entity.NullableDuration = null;
        entity.NullablePeriod = null;
        entity.NullableRange = null;
        entity.NullableCron = null;

        int id;
        using (var context = new AutoTemporalContext(options))
        {
            context.Database.EnsureCreated();
            context.Entities.Add(entity);
            context.SaveChanges();
            id = entity.Id;
        }

        using (var context = new AutoTemporalContext(options))
        {
            var loaded = context.Entities.Single(e => e.Id == id);

            Assert.Null(loaded.NullableDate);
            Assert.Null(loaded.NullableTime);
            Assert.Null(loaded.NullableWallDateTime);
            Assert.Null(loaded.NullableZoned);
            Assert.Null(loaded.NullableDuration);
            Assert.Null(loaded.NullablePeriod);
            Assert.Null(loaded.NullableRange);
            Assert.Null(loaded.NullableCron);
        }
    }
}
