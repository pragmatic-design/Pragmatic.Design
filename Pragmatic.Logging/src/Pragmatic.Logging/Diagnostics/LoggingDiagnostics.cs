using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Logging.Diagnostics;

/// <summary>
///     Centralized OTel diagnostics for Pragmatic.Logging: ActivitySource and Meter with instruments.
///     These complement the legacy <c>PragmaticLoggingEventCounters</c> (EventSource-based)
///     with cross-platform OTel Meter instruments.
/// </summary>
public static class LoggingDiagnostics
{
    /// <summary>The source name for all Logging activities.</summary>
    public const string SourceName = "Pragmatic.Logging";

    /// <summary>The ActivitySource for distributed tracing.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>The Meter for metrics collection.</summary>
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    /// <summary>Counter of log entries processed (throughput).</summary>
    public static readonly Counter<long> LogThroughput = Meter.CreateCounter<long>(
        "pragmatic.logging.throughput",
        unit: "entries",
        description: "Total log entries processed");

    /// <summary>Counter of log entries dropped due to back-pressure.</summary>
    public static readonly Counter<long> LogDrops = Meter.CreateCounter<long>(
        "pragmatic.logging.drops",
        description: "Total log entries dropped");

    /// <summary>Histogram of batch processing latency in milliseconds.</summary>
    public static readonly Histogram<double> BatchLatency = Meter.CreateHistogram<double>(
        "pragmatic.logging.batch_latency",
        unit: "ms",
        description: "Batch processing latency");

    /// <summary>Histogram of adaptive batch size.</summary>
    public static readonly Histogram<int> BatchSize = Meter.CreateHistogram<int>(
        "pragmatic.logging.batch_size",
        unit: "entries",
        description: "Adaptive batch size");
}
