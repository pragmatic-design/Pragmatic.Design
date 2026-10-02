namespace Pragmatic.Logging.Configuration;

/// <summary>
/// Rate limiting strategies for controlling log message throughput.
/// </summary>
public enum RateLimitStrategy
{
    /// <summary>
    /// Token bucket algorithm - allows bursts up to bucket capacity.
    /// Good balance between throughput and burst protection.
    /// </summary>
    TokenBucket = 0,

    /// <summary>
    /// Fixed window algorithm - resets counter at fixed intervals.
    /// Simple and predictable, but can allow bursts at window boundaries.
    /// </summary>
    FixedWindow = 1,

    /// <summary>
    /// Sliding window algorithm - maintains precise rate over time.
    /// Most accurate but requires more memory and computation.
    /// </summary>
    SlidingWindow = 2
}