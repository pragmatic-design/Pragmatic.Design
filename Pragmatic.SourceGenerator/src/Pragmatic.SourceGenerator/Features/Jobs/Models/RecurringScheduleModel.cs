namespace Pragmatic.SourceGenerator.Features.Jobs.Models;

/// <summary>
///     One <c>[RecurringJob]</c> on a job class: when it runs, under which name.
/// </summary>
/// <remarks>
///     <para>
///         A job may carry several. "Every day at 02:00 <b>and</b> every Monday at 06:00" is one job on
///         two clocks. An attribute that allowed only one would leave it no declarative form: the
///         author would write two classes with the same body, or one body that re-decides at run time
///         which occurrence this is.
///     </para>
///     <para>
///         ⚠️ What belongs here and what does not is decided by <c>RecurringJobDefinition</c>, the
///         runtime type this becomes: it carries an id, a cron, a timezone and a misfire policy — so
///         those are per schedule. <c>Priority</c> and <c>MaxConcurrency</c> are not on it; they
///         describe how the job type is scheduled and executed, and stay on the job.
///     </para>
/// </remarks>
internal sealed record RecurringScheduleModel
{
    /// <summary>The recurring definition's id: explicit, or derived from the class for the first one.</summary>
    public required string Id { get; init; }

    /// <summary>The cron expression, 5 or 6 part.</summary>
    public required string CronExpression { get; init; }

    /// <summary>IANA timezone the cron is evaluated in, or null for UTC.</summary>
    public string? TimeZoneId { get; init; }

    /// <summary>The misfire policy, as the <c>MisfirePolicy</c> enum's integer value.</summary>
    public int MisfirePolicy { get; init; }
}
