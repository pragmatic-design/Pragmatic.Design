namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Standard execution order for resilience strategies.
/// Lower = more external (wraps more of the pipeline).
/// </summary>
public static class StrategyOrder
{
    /// <summary>
    ///     Fallback is outermost — the last resort. It must wrap every other strategy so it can
    ///     convert whatever escapes them (RetryExhausted, CircuitBroken, TimeoutRejected,
    ///     BulkheadRejected, RateLimitRejected) into the fallback value. If it sat inside retry/CB
    ///     it would swallow each failure before they ever saw it, silently disabling them.
    /// </summary>
    public const int Fallback = 25;

    /// <summary>Rate limiter rejects fast before any strategy other than fallback.</summary>
    public const int RateLimiter = 50;

    /// <summary>Timeout wraps everything — cancels if total time exceeded.</summary>
    public const int Timeout = 100;

    /// <summary>Hedging launches parallel attempts, wraps the inner pipeline.</summary>
    public const int Hedging = 150;

    /// <summary>Bulkhead limits concurrency before other strategies.</summary>
    public const int Bulkhead = 200;

    /// <summary>Circuit breaker rejects fast if service is unhealthy.</summary>
    public const int CircuitBreaker = 300;

    /// <summary>Retry is innermost — closest to the operation, retries on transient failures.</summary>
    public const int Retry = 400;
}
