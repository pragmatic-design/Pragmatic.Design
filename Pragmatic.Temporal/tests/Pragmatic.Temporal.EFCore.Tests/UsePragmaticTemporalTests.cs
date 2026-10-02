using Microsoft.EntityFrameworkCore;
using Pragmatic.Temporal.EntityFrameworkCore;
using Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>
///     Regression tests for the historic no-op: UsePragmaticTemporal() alone must
///     configure the model, and TemporalEfCoreOptions must be honored.
/// </summary>
public class UsePragmaticTemporalTests
{
    [Fact]
    public void UsePragmaticTemporal_Alone_ConfiguresAllConverters()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<AutoTemporalContext>(
            b => b.UsePragmaticTemporal());
        using var _ = connection;
        using var context = new AutoTemporalContext(options);

        var entityType = context.Model.FindEntityType(typeof(TemporalEntity))!;

        Assert.IsType<LocalDateValueConverter>(entityType.FindProperty(nameof(TemporalEntity.Date))!.GetValueConverter());
        Assert.IsType<DurationToTicksConverter>(entityType.FindProperty(nameof(TemporalEntity.Duration))!.GetValueConverter());
        Assert.IsType<CronExpressionValueConverter>(entityType.FindProperty(nameof(TemporalEntity.Cron))!.GetValueConverter());
        Assert.IsType<NullableCronExpressionValueConverter>(entityType.FindProperty(nameof(TemporalEntity.NullableCron))!.GetValueConverter());
        Assert.IsType<ZonedDateTimeValueConverter>(entityType.FindProperty(nameof(TemporalEntity.Zoned))!.GetValueConverter());
    }

    [Fact]
    public void UsePragmaticTemporal_AppliesMaxLengths()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<AutoTemporalContext>(
            b => b.UsePragmaticTemporal());
        using var _ = connection;
        using var context = new AutoTemporalContext(options);

        var entityType = context.Model.FindEntityType(typeof(TemporalEntity))!;

        Assert.Equal(50, entityType.FindProperty(nameof(TemporalEntity.Period))!.GetMaxLength());
        Assert.Equal(50, entityType.FindProperty(nameof(TemporalEntity.Range))!.GetMaxLength());
        Assert.Equal(100, entityType.FindProperty(nameof(TemporalEntity.Cron))!.GetMaxLength());
        Assert.Equal(100, entityType.FindProperty(nameof(TemporalEntity.Zoned))!.GetMaxLength());
    }

    [Fact]
    public void StoreDurationAsTicksFalse_UsesTimeSpanConverters()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<TimeSpanDurationContext>(
            b => b.UsePragmaticTemporal(o => o.StoreDurationAsTicks = false));
        using var _ = connection;
        using var context = new TimeSpanDurationContext(options);

        var entityType = context.Model.FindEntityType(typeof(TemporalEntity))!;

        Assert.IsType<DurationToTimeSpanConverter>(entityType.FindProperty(nameof(TemporalEntity.Duration))!.GetValueConverter());
        Assert.IsType<NullableDurationToTimeSpanConverter>(entityType.FindProperty(nameof(TemporalEntity.NullableDuration))!.GetValueConverter());
    }

    [Fact]
    public void StoreDurationAsTicksFalse_RoundTrips()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<TimeSpanDurationContext>(
            b => b.UsePragmaticTemporal(o => o.StoreDurationAsTicks = false));
        using var _ = connection;

        var duration = Duration.FromHours(36.5);
        int id;
        using (var context = new TimeSpanDurationContext(options))
        {
            context.Database.EnsureCreated();
            var entity = new TemporalEntity
            {
                Zoned = ZonedDateTime.FromUtc(DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc),
                Duration = duration,
                NullableDuration = duration
            };
            context.Entities.Add(entity);
            context.SaveChanges();
            id = entity.Id;
        }

        using (var context = new TimeSpanDurationContext(options))
        {
            var loaded = context.Entities.Single(e => e.Id == id);
            Assert.Equal(duration, loaded.Duration);
            Assert.Equal(duration, loaded.NullableDuration);
        }
    }

    [Fact]
    public void ApplyToAllPropertiesFalse_LeavesTemporalTypesUnmapped()
    {
        var (connection, options) = SqliteContextFactory.CreateOptions<OptOutContext>(
            b => b.UsePragmaticTemporal(o => o.ApplyToAllProperties = false));
        using var _ = connection;
        using var context = new OptOutContext(options);

        // LocalDate has no converter and no provider mapping → model build must fail loudly
        Assert.Throws<InvalidOperationException>(() => context.Model);
    }
}
