using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pragmatic.Messaging.Diagnostics;

/// <summary>
///     Centralized diagnostics for Pragmatic.Messaging: ActivitySource and Meter with instruments.
/// </summary>
public static class MessagingDiagnostics
{
    /// <summary>The source name for all Messaging activities.</summary>
    public const string SourceName = "Pragmatic.Messaging";

    /// <summary>The ActivitySource for distributed tracing.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");

    /// <summary>The Meter for metrics collection.</summary>
    public static readonly Meter Meter = new(SourceName, "1.0.0");

    /// <summary>Counter of total messages published.</summary>
    public static readonly Counter<long> MessagesPublished = Meter.CreateCounter<long>(
        "pragmatic.messaging.published",
        description: "Total messages published");

    /// <summary>Counter of handler failures.</summary>
    public static readonly Counter<long> HandlerFailures = Meter.CreateCounter<long>(
        "pragmatic.messaging.handler_failures",
        description: "Total message handler failures");

    /// <summary>Histogram of handler execution duration in milliseconds.</summary>
    public static readonly Histogram<double> HandlerDuration = Meter.CreateHistogram<double>(
        "pragmatic.messaging.handler_duration",
        unit: "ms",
        description: "Duration of individual message handler execution");

    /// <summary>Counter of messages sent to dead letter.</summary>
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>(
        "pragmatic.messaging.dead_lettered",
        description: "Total messages sent to dead letter store");

    /// <summary>Histogram of outbox delivery duration in milliseconds.</summary>
    public static readonly Histogram<double> OutboxDeliveryDuration = Meter.CreateHistogram<double>(
        "pragmatic.messaging.outbox_delivery_duration",
        unit: "ms",
        description: "Duration of outbox message delivery batch");

    /// <summary>Counter of retry attempts across all handlers.</summary>
    public static readonly Counter<long> RetryAttempts = Meter.CreateCounter<long>(
        "pragmatic.messaging.retry_attempts",
        description: "Total retry attempts");

    /// <summary>Counter of circuit breaker trips (open state entered).</summary>
    public static readonly Counter<long> CircuitBreakerTrips = Meter.CreateCounter<long>(
        "pragmatic.messaging.circuit_breaker_trips",
        description: "Total circuit breaker open events");

    /// <summary>Counter of deduplicated messages (idempotency).</summary>
    public static readonly Counter<long> IdempotencyDuplicates = Meter.CreateCounter<long>(
        "pragmatic.messaging.idempotency_duplicates",
        description: "Total duplicate messages skipped by idempotency");

    /// <summary>Counter of payloads moved to the claim check store.</summary>
    public static readonly Counter<long> ClaimChecks = Meter.CreateCounter<long>(
        "pragmatic.messaging.claim_checks",
        description: "Total payloads offloaded via the claim check pattern");
}
