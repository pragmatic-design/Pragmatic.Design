using Pragmatic.SourceGenerator.Core;
using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Jobs.Models;

/// <summary>
///     Model for a class decorated with [RecurringJob] or [Job].
/// </summary>
internal sealed record JobModel : GeneratorModel
{
    /// <summary>Whether this is a recurring job (has [RecurringJob]).</summary>
    public bool IsRecurring { get; init; }

    /// <summary>Cron expression from [RecurringJob]. Null for non-recurring.</summary>
    public string? CronExpression { get; init; }

    /// <summary>
    ///     Every schedule this job declares, in the order the attributes appear.
    /// </summary>
    /// <remarks>
    ///     The single-schedule fields above are what the rest of the pipeline still reads for a job's
    ///     first (or only) schedule; this is what the recurring provider enumerates. A job with one
    ///     [RecurringJob] has exactly one entry here, which is why adding the list changed no output
    ///     for any existing job.
    /// </remarks>
    /// <summary>
    ///     The schedules to emit: the declared ones, or the single one the flat fields describe.
    /// </summary>
    /// <remarks>
    ///     ⚠️ There are two producers of a <see cref="JobModel" /> — the transform that reads
    ///     <c>[RecurringJob]</c> from source, and <c>TraitJobModelBuilder</c>, which scaffolds the purge
    ///     job for <c>[Attachment]</c>. The second knows nothing about <c>Schedules</c>: when the list
    ///     was read directly, the scaffolded job silently vanished from the recurring provider, which is
    ///     the same shape as every other "one truth, N hand-written copies" defect in this repository.
    ///     Answering here means a producer that only fills the flat fields is still right, and a third
    ///     one cannot reintroduce the hole.
    /// </remarks>
    public EquatableArray<RecurringScheduleModel> EffectiveSchedules =>
        Schedules.Length > 0 || string.IsNullOrEmpty(CronExpression)
            ? Schedules
            : new[]
            {
                new RecurringScheduleModel
                {
                    Id = RecurringJobId ?? string.Empty,
                    CronExpression = CronExpression!,
                    TimeZoneId = TimeZoneId,
                    MisfirePolicy = MisfirePolicy,
                },
            }.ToImmutableArray();

    /// <summary>
    ///     True when the job declares more than one schedule and more than one of them relies on the
    ///     id derived from the class name - which is the collision PRAG2507 reports.
    /// </summary>
    public bool HasUnnamedExtraSchedule { get; init; }

    public EquatableArray<RecurringScheduleModel> Schedules { get; init; } =
        EquatableArray<RecurringScheduleModel>.Empty;

    /// <summary>Recurring job ID. Derived from class name if not specified.</summary>
    public string? RecurringJobId { get; init; }

    /// <summary>IANA timezone for cron evaluation. Null = UTC.</summary>
    public string? TimeZoneId { get; init; }

    /// <summary>Whether the class is partial (required for LoggerMessage).</summary>
    public bool IsPartial { get; init; }

    // ── Parameter type (from IJob<TParams>) ──

    /// <summary>FQN of the parameter type. Null for IJob (parameterless).</summary>
    public string? ParameterTypeFqn { get; init; }

    /// <summary>Short name of the parameter type.</summary>
    public string? ParameterTypeShortName { get; init; }

    /// <summary>Whether the job has typed parameters (implements IJob&lt;T&gt;).</summary>
    public bool HasParameters { get; init; }

    // ── Retry ──

    public bool HasRetry { get; init; }
    public int RetryMaxAttempts { get; init; }
    public int RetryStrategy { get; init; }
    public int RetryBaseDelayMs { get; init; }

    // ── Timeout ──

    public bool HasTimeout { get; init; }
    public int TimeoutSeconds { get; init; }

    // ── Continuation ──

    /// <summary>FQN of continuation job from [ContinueWith&lt;T&gt;]. Null if none.</summary>
    public string? ContinuationJobTypeFqn { get; init; }

    /// <summary>Whether the job class implements <c>IJob</c> or <c>IJob&lt;TParams&gt;</c> (PRAG2500).</summary>
    public bool ImplementsJobInterface { get; init; }

    /// <summary>Whether the <c>[Continuation&lt;T&gt;]</c> target is itself a job (PRAG2505).</summary>
    public bool ContinuationImplementsJob { get; init; }

    /// <summary>Misfire policy for a recurring job, as the <c>MisfirePolicy</c> enum's integer value.</summary>
    public int MisfirePolicy { get; init; }

    /// <summary>Declared scheduling priority (0 = default).</summary>
    public int Priority { get; init; }

    /// <summary>Declared per-host max concurrency for this job type (0 = unbounded).</summary>
    public int MaxConcurrency { get; init; }

    /// <summary>
    ///     What this job's constructor asks the container for, read exactly as a <c>[Service]</c>'s is.
    /// </summary>
    /// <remarks>
    ///     A job is resolved from the container like any service — the generated registration writes
    ///     <c>TryAddScoped&lt;TJob&gt;()</c> — so what it takes is checked too, not only what a
    ///     <c>[Service]</c> takes. A missing registration would otherwise surface
    ///     when the scheduler first ran the job, in a background worker, as a log line on a schedule —
    ///     the one place it is hardest to notice, while the `[Service]` beside it was refused at build
    ///     time.
    /// </remarks>
    public EquatableArray<Composition.Models.DependencyModel> Dependencies { get; init; } =
        EquatableArray<Composition.Models.DependencyModel>.Empty;

    /// <summary>Location for diagnostics.</summary>
    public LocationInfo? LocationInfo { get; init; }
    public Microsoft.CodeAnalysis.Location? Location => LocationInfo?.ToLocation();
}
