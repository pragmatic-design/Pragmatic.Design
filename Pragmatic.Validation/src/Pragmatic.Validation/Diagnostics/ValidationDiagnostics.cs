using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Validation.Diagnostics;

/// <summary>
///     Centralized diagnostics for Pragmatic.Validation: ActivitySource and Meter with instruments.
/// </summary>
public static class ValidationDiagnostics
{
    /// <summary>The source name for all Validation activities.</summary>
    public const string SourceName = "Pragmatic.Validation";

    /// <summary>The ActivitySource for distributed tracing.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>The Meter for metrics collection.</summary>
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    /// <summary>Histogram of validation duration in milliseconds.</summary>
    public static readonly Histogram<double> ValidationDuration = Meter.CreateHistogram<double>(
        "pragmatic.validation.duration",
        unit: "ms",
        description: "Duration of validation execution");

    /// <summary>Counter of total validation executions.</summary>
    public static readonly Counter<long> ValidationExecutions = Meter.CreateCounter<long>(
        "pragmatic.validation.executions",
        description: "Total validation executions");

    /// <summary>Counter of validation failures (at least one issue).</summary>
    public static readonly Counter<long> ValidationFailures = Meter.CreateCounter<long>(
        "pragmatic.validation.failures",
        description: "Total validation failures");
}
