namespace Pragmatic.Endpoints.Configuration;

/// <summary>
///     Available rate limiting strategies, matching ASP.NET Core's built-in limiters.
/// </summary>
public enum RateLimiterStrategy
{
    /// <summary>Fixed time window with permit limit.</summary>
    FixedWindow,

    /// <summary>Sliding window divided into segments.</summary>
    SlidingWindow,

    /// <summary>Token bucket with periodic replenishment.</summary>
    TokenBucket,

    /// <summary>Maximum concurrent requests (no time window).</summary>
    Concurrency
}
