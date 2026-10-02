namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Options for the rate limiter (requests per time window) strategy.
/// </summary>
public sealed class RateLimiterOptions
{
    /// <summary>Maximum number of requests allowed within the window. Default: 100.</summary>
    public int MaxRequests { get; set; } = 100;

    /// <summary>Sliding window duration. Default: 1 minute.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
