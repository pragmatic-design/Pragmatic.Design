namespace Pragmatic.Jobs.Configuration;

/// <summary>
///     Configuration options for the job processing infrastructure.
/// </summary>
public sealed class JobsOptions
{
    /// <summary>Polling interval in seconds for job processor. Default: 5.</summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    ///     How long the processor waits before its first poll, so an application finishes starting
    ///     before jobs begin running. Seconds; zero polls immediately.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It was two seconds, written into the loop, and nothing could shorten it. A test that
    ///     drives the real processor then pays it out of its own deadline, and under the gate — where
    ///     a hundred suites share the machine — the wait for the first job body went past thirty
    ///     seconds and the run went red on a number rather than on a defect. A value the caller can
    ///     set is what makes that measurable instead of arguable.
    /// </remarks>
    public int StartupDelaySeconds { get; set; } = 2;

    /// <summary>Number of concurrent job processing tasks. Default: 2.</summary>
    public int WorkerCount { get; set; } = 2;

    /// <summary>Lease duration in seconds for distributed locking. Default: 300 (5 min).</summary>
    public int LeaseTimeSeconds { get; set; } = 300;

    /// <summary>Maximum pending jobs to fetch per poll cycle. Default: 10.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>Default max retry attempts for jobs without [Retry]. Default: 1 (no retry).</summary>
    public int DefaultMaxRetries { get; set; } = 1;

    /// <summary>Worker ID for lease acquisition. Auto-generated if null.</summary>
    public string? WorkerId { get; set; }

    /// <summary>
    ///     How long a finished job (Completed / Failed / Cancelled) is kept before being deleted.
    ///     Default: 30 days. Set to 0 to disable purging and retain job history indefinitely.
    /// </summary>
    /// <remarks>
    ///     Retention matters beyond table size: a job row keeps its serialized parameters, which
    ///     commonly carry personal data. Disabling the purge keeps that payload forever.
    /// </remarks>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Maximum rows deleted per purge pass. Default: 1000.</summary>
    public int PurgeBatchSize { get; set; } = 1000;

    /// <summary>
    ///     How far behind its scheduled occurrence a recurring job may be before it counts as a
    ///     misfire (a run missed because the host was down), triggering its <c>MisfirePolicy</c>.
    ///     A due job within this window is treated as a normal, slightly-late run. Default: 1 minute.
    /// </summary>
    public TimeSpan MisfireThreshold { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Whether to use EF Core persistence. Default: false (InMemory).</summary>
    public bool UseEfCore { get; set; }
}
