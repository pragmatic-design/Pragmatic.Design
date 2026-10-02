using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>Entity exercising every temporal type (plus nullable variants).</summary>
public class TemporalEntity
{
    public int Id { get; set; }

    public LocalDate Date { get; set; }
    public LocalDate? NullableDate { get; set; }

    public LocalTime Time { get; set; }
    public LocalTime? NullableTime { get; set; }

    public LocalDateTime WallDateTime { get; set; }
    public LocalDateTime? NullableWallDateTime { get; set; }

    public ZonedDateTime Zoned { get; set; }
    public ZonedDateTime? NullableZoned { get; set; }

    public Duration Duration { get; set; }
    public Duration? NullableDuration { get; set; }

    public Period Period { get; set; }
    public Period? NullablePeriod { get; set; }

    public DateRange Range { get; set; }
    public DateRange? NullableRange { get; set; }

    public CronExpression Cron { get; set; } = CronExpression.EveryMinute;
    public CronExpression? NullableCron { get; set; }
}
