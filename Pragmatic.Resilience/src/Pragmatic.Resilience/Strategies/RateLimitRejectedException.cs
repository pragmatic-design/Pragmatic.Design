namespace Pragmatic.Resilience.Strategies;

/// <summary>
/// Thrown when a request is rejected because the rate limit has been exceeded.
/// </summary>
public sealed class RateLimitRejectedException(int maxRequests, TimeSpan window)
    : Exception($"Rate limit exceeded: {maxRequests} requests per {window.TotalSeconds:F0}s window")
{
    /// <summary>The maximum number of requests allowed in the window.</summary>
    public int MaxRequests { get; } = maxRequests;

    /// <summary>The sliding window duration.</summary>
    public TimeSpan Window { get; } = window;
}
