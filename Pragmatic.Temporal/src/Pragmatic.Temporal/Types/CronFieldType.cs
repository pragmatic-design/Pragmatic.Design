namespace Pragmatic.Temporal.Types;

/// <summary>
///     Represents the type of a cron expression field.
/// </summary>
internal enum CronFieldType
{
    Seconds,
    Minutes,
    Hours,
    DayOfMonth,
    Month,
    DayOfWeek
}
