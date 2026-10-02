namespace Pragmatic.Jobs.Attributes;

/// <summary>
///     Marks a class as a recurring background job with a cron schedule.
///     The SG generates an invoker, registers the job, and creates a recurring definition.
/// </summary>
/// <param name="cronExpression">Cron expression (5 or 6 part). E.g., "0 2 * * *" = daily at 2 AM.</param>
// AllowMultiple: a job may run on more than one clock. "Every day at 02:00 and every Monday at
// 06:00" is one job with two schedules, and with a single attribute the only forms available were two
// classes with the same body, or one body that re-decided at run time which occurrence this was.
// Each attribute becomes its own RecurringJobDefinition; the second one on has to carry an Id, or the
// generator reports PRAG2507 rather than inventing a name for it.
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class RecurringJobAttribute(string cronExpression) : Attribute
{
    /// <summary>Cron expression for scheduling.</summary>
    public string CronExpression { get; } = cronExpression;

    /// <summary>
    ///     Unique recurring job ID. Defaults to kebab-cased class name
    ///     (e.g., DailyReportJob → "daily-report").
    /// </summary>
    public string? Id { get; set; }

    /// <summary>IANA timezone for cron evaluation. Null = UTC.</summary>
    public string? TimeZone { get; set; }

    /// <summary>
    ///     What to do with an occurrence missed while the host was down. Defaults to
    ///     <see cref="MisfirePolicy.RunOnce"/> (run the missed occurrence once, then resume).
    /// </summary>
    public MisfirePolicy Misfire { get; set; } = MisfirePolicy.RunOnce;

    /// <summary>
    ///     Scheduling priority: due jobs with a higher value are picked up before lower ones,
    ///     ties broken by scheduled time. Default: 0.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///     Maximum instances of this job type a single host runs at once. 0 (default) means only the
    ///     global worker count bounds it.
    /// </summary>
    public int MaxConcurrency { get; set; }
}
