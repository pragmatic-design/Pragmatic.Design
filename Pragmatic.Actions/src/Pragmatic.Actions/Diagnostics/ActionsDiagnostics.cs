using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Actions.Diagnostics;

/// <summary>
///     Centralized diagnostics for Pragmatic.Actions: ActivitySource and Meter with instruments.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ActivitySource"/> and <see cref="Meter"/> are static <see cref="IDisposable"/>
///         instances that live for the lifetime of the process. They are intentionally NOT
///         registered for disposal during DI shutdown: telemetry plumbing must remain available
///         until the very last log line is flushed, which is after host shutdown completes. The
///         CLR finalizers reclaim them on process exit. This mirrors the standard
///         <c>System.Diagnostics</c> usage pattern (e.g. ASP.NET Core's own diagnostics).
///     </para>
/// </remarks>
public static class ActionsDiagnostics
{
    /// <summary>The source name for all Actions activities.</summary>
    public const string SourceName = "Pragmatic.Actions";

    /// <summary>The ActivitySource for distributed tracing.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>The Meter for metrics collection.</summary>
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    // =========================================================================
    // Instruments — Actions
    // =========================================================================

    /// <summary>Histogram of action execution duration in milliseconds.</summary>
    public static readonly Histogram<double> ActionDuration = Meter.CreateHistogram<double>(
        "pragmatic.actions.duration",
        unit: "ms",
        description: "Duration of action execution");

    /// <summary>Counter of total action invocations.</summary>
    public static readonly Counter<long> ActionInvocations = Meter.CreateCounter<long>(
        "pragmatic.actions.invocations",
        description: "Total action invocations");

    /// <summary>Counter of total action failures (business errors).</summary>
    public static readonly Counter<long> ActionFailures = Meter.CreateCounter<long>(
        "pragmatic.actions.failures",
        description: "Total action failures");

    /// <summary>Counter of filter short-circuits (action not executed).</summary>
    public static readonly Counter<long> FilterShortCircuits = Meter.CreateCounter<long>(
        "pragmatic.actions.filter_short_circuits",
        description: "Total filter short-circuits");

    // =========================================================================
    // Instruments — Mutations
    // =========================================================================

    /// <summary>Histogram of mutation execution duration in milliseconds.</summary>
    public static readonly Histogram<double> MutationDuration = Meter.CreateHistogram<double>(
        "pragmatic.mutations.duration",
        unit: "ms",
        description: "Duration of mutation execution");

    /// <summary>Counter of total mutation invocations.</summary>
    public static readonly Counter<long> MutationInvocations = Meter.CreateCounter<long>(
        "pragmatic.mutations.invocations",
        description: "Total mutation invocations");

    /// <summary>Counter of total mutation failures.</summary>
    public static readonly Counter<long> MutationFailures = Meter.CreateCounter<long>(
        "pragmatic.mutations.failures",
        description: "Total mutation failures");

    /// <summary>Counter of mutation validation failures (L1 + L2).</summary>
    public static readonly Counter<long> MutationValidationFailures = Meter.CreateCounter<long>(
        "pragmatic.mutations.validation_failures",
        description: "Total mutation validation failures");
}
