using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Resilience.Diagnostics;

/// <summary>
///     Centralized diagnostics for Pragmatic.Resilience: ActivitySource and Meter with instruments.
/// </summary>
public static class ResilienceDiagnostics
{
    /// <summary>The source name for all Resilience activities.</summary>
    public const string SourceName = "Pragmatic.Resilience";

    /// <summary>The ActivitySource for distributed tracing.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>The Meter for metrics collection.</summary>
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    /// <summary>Histogram of pipeline execution duration in milliseconds.</summary>
    public static readonly Histogram<double> PipelineDuration = Meter.CreateHistogram<double>(
        "pragmatic.resilience.duration",
        unit: "ms",
        description: "Duration of resilience pipeline execution");

    /// <summary>Counter of total pipeline executions.</summary>
    public static readonly Counter<long> PipelineExecutions = Meter.CreateCounter<long>(
        "pragmatic.resilience.executions",
        description: "Total resilience pipeline executions");

    /// <summary>Counter of retry attempts.</summary>
    public static readonly Counter<long> RetryAttempts = Meter.CreateCounter<long>(
        "pragmatic.resilience.retry_attempts",
        description: "Total retry attempts");

    /// <summary>Counter of circuit breaker rejections.</summary>
    public static readonly Counter<long> CircuitRejections = Meter.CreateCounter<long>(
        "pragmatic.resilience.circuit_rejections",
        description: "Total circuit breaker rejections");

    /// <summary>Counter of timeout occurrences.</summary>
    public static readonly Counter<long> Timeouts = Meter.CreateCounter<long>(
        "pragmatic.resilience.timeouts",
        description: "Total timeout occurrences");

    /// <summary>Counter of bulkhead rejections.</summary>
    public static readonly Counter<long> BulkheadRejections = Meter.CreateCounter<long>(
        "pragmatic.resilience.bulkhead_rejections",
        description: "Total bulkhead rejections");

    /// <summary>Counter of hedging attempts (additional parallel requests).</summary>
    public static readonly Counter<long> HedgingAttempts = Meter.CreateCounter<long>(
        "pragmatic.resilience.hedging_attempts",
        description: "Total hedging attempts launched");

    /// <summary>Counter of hedging successes (first-wins resolved).</summary>
    public static readonly Counter<long> HedgingSuccesses = Meter.CreateCounter<long>(
        "pragmatic.resilience.hedging_successes",
        description: "Total hedging first-wins");

    /// <summary>Counter of rate limit rejections.</summary>
    public static readonly Counter<long> RateLimitRejections = Meter.CreateCounter<long>(
        "pragmatic.resilience.rate_limit_rejections",
        description: "Total rate limit rejections");
}
