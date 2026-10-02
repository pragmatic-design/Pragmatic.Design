using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Jobs.Diagnostics;

/// <summary>
///     Centralized diagnostics for Pragmatic.Jobs: ActivitySource and Meter.
/// </summary>
public static class JobsDiagnostics
{
    public const string SourceName = "Pragmatic.Jobs";

    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    public static readonly Counter<long> JobsEnqueued = Meter.CreateCounter<long>(
        "pragmatic.jobs.enqueued", description: "Total jobs enqueued");

    public static readonly Counter<long> JobsCompleted = Meter.CreateCounter<long>(
        "pragmatic.jobs.completed", description: "Total jobs completed successfully");

    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>(
        "pragmatic.jobs.failed", description: "Total jobs that failed permanently");

    public static readonly Counter<long> JobsRetried = Meter.CreateCounter<long>(
        "pragmatic.jobs.retried", description: "Total retry attempts");

    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "pragmatic.jobs.duration", unit: "ms", description: "Job execution duration");

    public static readonly Counter<long> LeaseAcquisitions = Meter.CreateCounter<long>(
        "pragmatic.jobs.lease_acquisitions", description: "Total lease acquisitions");

    public static readonly Counter<long> LeaseConflicts = Meter.CreateCounter<long>(
        "pragmatic.jobs.lease_conflicts", description: "Total lease acquisition conflicts (another worker)");

    public static readonly Counter<long> RecurringJobsTriggered = Meter.CreateCounter<long>(
        "pragmatic.jobs.recurring_triggered", description: "Total recurring job triggers");
}
