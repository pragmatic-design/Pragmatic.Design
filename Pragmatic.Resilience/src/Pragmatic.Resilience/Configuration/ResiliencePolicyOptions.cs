using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Configuration;

/// <summary>
/// Configuration for a single resilience policy (named pipeline).
/// Bind from appsettings.json or configure fluently.
/// </summary>
public sealed class ResiliencePolicyOptions
{
    /// <summary>Retry options. Null = no retry.</summary>
    public RetryOptions? Retry { get; set; }

    /// <summary>Timeout options. Null = no timeout.</summary>
    public TimeoutOptions? Timeout { get; set; }

    /// <summary>Circuit breaker options. Null = no circuit breaker.</summary>
    public CircuitBreakerOptions? CircuitBreaker { get; set; }

    /// <summary>Bulkhead options. Null = no bulkhead.</summary>
    public BulkheadOptions? Bulkhead { get; set; }

    /// <summary>Hedging options. Null = no hedging.</summary>
    public HedgingOptions? Hedging { get; set; }

    /// <summary>Rate limiter options. Null = no rate limiting.</summary>
    public RateLimiterOptions? RateLimiter { get; set; }

    // NOTE: Fallback is intentionally not configurable here. A fallback requires a typed
    // fallback value/factory (FallbackOptions&lt;TResult&gt;) that cannot be expressed in
    // configuration. Wire it via the fluent builder: AddPolicy(name, b =&gt; b.AddFallback(...)).
}
