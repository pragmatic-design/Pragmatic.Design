using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Events.Diagnostics;

/// <summary>
///     Centralized diagnostics for Pragmatic.Events: ActivitySource and Meter with instruments.
/// </summary>
/// <remarks>
///     <see cref="ActivitySource" /> and <see cref="Meter" /> are app-lifetime singletons and are
///     not disposed during normal operation. Call <see cref="DisposeForTesting" /> in test teardown
///     to avoid conflicts between test runs that share the same process.
/// </remarks>
public static class EventsDiagnostics
{
    /// <summary>The source name for all Events activities.</summary>
    public const string SourceName = "Pragmatic.Events";

    /// <summary>The ActivitySource for distributed tracing.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>The Meter for metrics collection.</summary>
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    /// <summary>Histogram of event dispatch duration in milliseconds.</summary>
    public static readonly Histogram<double> DispatchDuration = Meter.CreateHistogram<double>(
        "pragmatic.events.dispatch_duration",
        unit: "ms",
        description: "Duration of event dispatch (all handlers)");

    /// <summary>Counter of total events dispatched.</summary>
    public static readonly Counter<long> EventsDispatched = Meter.CreateCounter<long>(
        "pragmatic.events.dispatched",
        description: "Total domain events dispatched");

    /// <summary>Counter of handler failures.</summary>
    public static readonly Counter<long> HandlerFailures = Meter.CreateCounter<long>(
        "pragmatic.events.handler_failures",
        description: "Total event handler failures");

    /// <summary>
    ///     Disposes <see cref="ActivitySource" /> and <see cref="Meter" />.
    ///     Intended for test teardown only — do not call in production.
    /// </summary>
    public static void DisposeForTesting()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}
