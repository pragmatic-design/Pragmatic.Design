using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Messaging.Models;

/// <summary>
///     Model for a class decorated with [MessageHandler].
/// </summary>
internal sealed record MessageHandlerModel : GeneratorModel
{
    /// <summary>FQN of the message type being handled (from IMessageHandler&lt;T&gt;).</summary>
    public required string MessageTypeFqn { get; init; }

    /// <summary>Short name of the message type (for display/logging).</summary>
    public required string MessageTypeShortName { get; init; }

    /// <summary>
    ///     Whether the message type also implements <c>Pragmatic.Events.IDomainEvent</c>.
    /// </summary>
    /// <remarks>
    ///     The registration bridges a message handler to <c>IDomainEventHandler&lt;T&gt;</c> so that a
    ///     domain event dispatched in process reaches it too, and both that interface and the adapter
    ///     behind it are constrained to <c>IDomainEvent</c>. The bridge was emitted for every message
    ///     type, so a cross-service contract — a record on a broker, with no reason to be a domain event —
    ///     produced two CS0311 inside a generated file. The model
    ///     answers the question the template has to ask.
    /// </remarks>
    public bool MessageIsDomainEvent { get; init; }

    /// <summary>Owning assembly name — used to per-module-namespace the registration (avoids cross-module type clash).</summary>
    public string AssemblyName { get; init; } = "";

    /// <summary>Handler execution order.</summary>
    public int Order { get; init; }

    /// <summary>Whether the handler class is declared partial.</summary>
    public bool IsPartial { get; init; }

    // Resilience configuration (from attributes)

    /// <summary>True if [Retry] is present.</summary>
    public bool HasRetry { get; init; }

    /// <summary>Max retry attempts.</summary>
    public int RetryMaxAttempts { get; init; }

    /// <summary>Retry backoff strategy (0=Fixed, 1=Exponential, 2=ExponentialWithJitter).</summary>
    public int RetryStrategy { get; init; }

    /// <summary>Base delay in ms between retries.</summary>
    public int RetryBaseDelayMs { get; init; }

    /// <summary>True if [CircuitBreaker] is present.</summary>
    public bool HasCircuitBreaker { get; init; }

    /// <summary>Failure threshold before circuit opens.</summary>
    public int CbFailureThreshold { get; init; }

    /// <summary>Break duration in seconds.</summary>
    public int CbBreakDurationSeconds { get; init; }

    /// <summary>True if [Timeout] is present.</summary>
    public bool HasTimeout { get; init; }

    /// <summary>Timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>True if [Redelivery] is present — persistent delayed redelivery via IMessageScheduler.</summary>
    public bool HasRedelivery { get; init; }

    /// <summary>Max redeliveries before giving up.</summary>
    public int RedeliveryMaxAttempts { get; init; }

    /// <summary>Base redelivery delay in seconds (exponential per redelivery).</summary>
    public int RedeliveryBaseDelaySeconds { get; init; }

    /// <summary>True if [ConcurrencyLimit] is present.</summary>
    public bool HasConcurrencyLimit { get; init; }

    /// <summary>Max concurrent executions.</summary>
    public int MaxConcurrent { get; init; }

    /// <summary>True if [RateLimit] is present.</summary>
    public bool HasRateLimit { get; init; }

    /// <summary>Executions allowed per rate window.</summary>
    public int RatePermitsPerPeriod { get; init; }

    /// <summary>Rate window length in seconds.</summary>
    public int RatePeriodSeconds { get; init; }

    // No [PartitionKey] fields here: a handler reporting the partition key of the type it consumes
    // would put the generated resolver in the consuming assembly, while the bus resolves it in the
    // publishing one. DeclaredPartitionKeyTransform reads
    // the attribute where the message is declared instead.


    /// <summary>
    ///     Equatable location snapshot for diagnostics reporting. Stores only primitive
    ///     data so the incremental model stays fully equatable (no Roslyn Location stored).
    /// </summary>
    public LocationInfo? LocationInfo { get; init; }

    /// <summary>Rebuilds a Roslyn Location for diagnostics. Computed — not part of equality.</summary>
    public Microsoft.CodeAnalysis.Location? Location => LocationInfo?.ToLocation();

    // Cross-cutting feature flags (from DetectedFeatures, embedded for caching correctness)

    /// <summary>True if Pragmatic.Validation is referenced — enables pre-handler validation.</summary>
    public bool FeatureHasValidation { get; init; }

    /// <summary>True if Pragmatic.MultiTenancy is referenced — enables tenant context propagation.</summary>
    public bool FeatureHasMultiTenancy { get; init; }

    /// <summary>True if Pragmatic.Authorization is referenced — enables ICallContext bypass.</summary>
    public bool FeatureHasAuthorization { get; init; }

    /// <summary>Named bus from [OnBus("name")] attribute. Null = default bus.</summary>
    public string? BusName { get; init; }
}
