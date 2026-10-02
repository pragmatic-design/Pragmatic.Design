using System.Runtime.CompilerServices;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// High-performance rate limiter using System.Threading.RateLimiting for optimal performance.
/// Provides various rate limiting strategies optimized for logging scenarios.
/// </summary>
public static class HighPerformanceRateLimiter
{
    /// <summary>
    /// Rate limiting strategy options.
    /// </summary>
    public enum RateLimitStrategy
    {
        /// <summary>
        /// Token bucket algorithm - allows bursts but maintains average rate.
        /// Best for most logging scenarios.
        /// </summary>
        TokenBucket,

        /// <summary>
        /// Sliding window algorithm - precise rate limiting over time.
        /// Best for strict rate limiting requirements.
        /// </summary>
        SlidingWindow,

        /// <summary>
        /// Fixed window algorithm - simple and fast.
        /// Best for high-performance scenarios where approximate limiting is acceptable.
        /// </summary>
        FixedWindow
    }

    /// <summary>
    /// Creates a high-performance FilterResult with rate limiting.
    /// </summary>
    /// <param name="strategy">Rate limiting strategy</param>
    /// <param name="maxMessages">Maximum messages allowed</param>
    /// <param name="timeWindow">Time window for rate limiting</param>
    /// <returns>FilterResult with optimized rate limiting</returns>
    public static FilterResult CreateRateLimitedFilter(RateLimitStrategy strategy, int maxMessages, TimeSpan timeWindow)
    {
        var rateLimiter = CreateRateLimiter(strategy, maxMessages, timeWindow);
        return new FilterResult((entry, context) => TryAcquire(rateLimiter));
    }

    /// <summary>
    /// Creates a rate limiter for the specified strategy.
    /// Each strategy has a dedicated thread-safe implementation:
    /// <list type="bullet">
    /// <item><see cref="RateLimitStrategy.TokenBucket"/> — burst-tolerant, average-rate (continuous refill).</item>
    /// <item><see cref="RateLimitStrategy.SlidingWindow"/> — precise: at most N within any trailing window.</item>
    /// <item><see cref="RateLimitStrategy.FixedWindow"/> — cheap counter reset at window boundaries.</item>
    /// </list>
    /// </summary>
    /// <param name="strategy">Rate limiting strategy</param>
    /// <param name="maxMessages">Maximum messages allowed</param>
    /// <param name="timeWindow">Time window for rate limiting</param>
    /// <returns>Configured rate limiter</returns>
    private static IRateLimiterStrategy CreateRateLimiter(RateLimitStrategy strategy, int maxMessages, TimeSpan timeWindow)
        => strategy switch
        {
            RateLimitStrategy.TokenBucket => new TokenBucketRateLimiter(maxMessages, timeWindow),
            RateLimitStrategy.SlidingWindow => new SlidingWindowRateLimiter(maxMessages, timeWindow),
            RateLimitStrategy.FixedWindow => new FixedWindowRateLimiter(maxMessages, timeWindow),
            _ => throw new NotSupportedException($"Rate limit strategy '{strategy}' is not supported.")
        };

    /// <summary>
    /// Ultra-fast rate limit check with minimal overhead.
    /// </summary>
    /// <param name="rateLimiter">The rate limiter to check</param>
    /// <returns>True if the message should be allowed</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryAcquire(IRateLimiterStrategy rateLimiter)
    {
        return rateLimiter.ShouldAllow();
    }
}

/// <summary>
/// Extension methods for FilterExpressionContext to use high-performance rate limiting.
/// </summary>
public static class FilterExpressionContextRateLimitExtensions
{
    /// <param name="context">Filter expression context</param>
    extension(FilterExpressionContext context)
    {
        /// <summary>
        /// Applies high-performance rate limiting with token bucket algorithm (recommended).
        /// </summary>
        /// <param name="maxMessages">Maximum messages allowed</param>
        /// <param name="timeWindow">Time window for rate limiting</param>
        /// <returns>FilterResult with optimized rate limiting</returns>
        public FilterResult RateLimitTokenBucket(int maxMessages, TimeSpan timeWindow)
        {
            return HighPerformanceRateLimiter.CreateRateLimitedFilter(
                HighPerformanceRateLimiter.RateLimitStrategy.TokenBucket, maxMessages, timeWindow);
        }

        /// <summary>
        /// Applies high-performance rate limiting with sliding window algorithm.
        /// </summary>
        /// <param name="maxMessages">Maximum messages allowed</param>
        /// <param name="timeWindow">Time window for rate limiting</param>
        /// <returns>FilterResult with precise rate limiting</returns>
        public FilterResult RateLimitSlidingWindow(int maxMessages, TimeSpan timeWindow)
        {
            return HighPerformanceRateLimiter.CreateRateLimitedFilter(
                HighPerformanceRateLimiter.RateLimitStrategy.SlidingWindow, maxMessages, timeWindow);
        }

        /// <summary>
        /// Applies high-performance rate limiting with fixed window algorithm.
        /// </summary>
        /// <param name="maxMessages">Maximum messages allowed</param>
        /// <param name="timeWindow">Time window for rate limiting</param>
        /// <returns>FilterResult with fast rate limiting</returns>
        public FilterResult RateLimitFixedWindow(int maxMessages, TimeSpan timeWindow)
        {
            return HighPerformanceRateLimiter.CreateRateLimitedFilter(
                HighPerformanceRateLimiter.RateLimitStrategy.FixedWindow, maxMessages, timeWindow);
        }
    }
}

/// <summary>
/// Configuration options for rate limiting in logging scenarios.
/// </summary>
public static class LoggingRateLimitPresets
{
    /// <summary>
    /// Preset for error logs - allows bursts but prevents spam.
    /// </summary>
    public static (HighPerformanceRateLimiter.RateLimitStrategy Strategy, int MaxMessages, TimeSpan Window) ErrorLogs =>
        (HighPerformanceRateLimiter.RateLimitStrategy.TokenBucket, 10, TimeSpan.FromMinutes(1));

    /// <summary>
    /// Preset for warning logs - moderate limiting.
    /// </summary>
    public static (HighPerformanceRateLimiter.RateLimitStrategy Strategy, int MaxMessages, TimeSpan Window) WarningLogs =>
        (HighPerformanceRateLimiter.RateLimitStrategy.TokenBucket, 50, TimeSpan.FromMinutes(1));

    /// <summary>
    /// Preset for debug logs - strict limiting to prevent spam.
    /// </summary>
    public static (HighPerformanceRateLimiter.RateLimitStrategy Strategy, int MaxMessages, TimeSpan Window) DebugLogs =>
        (HighPerformanceRateLimiter.RateLimitStrategy.SlidingWindow, 100, TimeSpan.FromSeconds(10));

    /// <summary>
    /// Preset for health check logs - very restrictive.
    /// </summary>
    public static (HighPerformanceRateLimiter.RateLimitStrategy Strategy, int MaxMessages, TimeSpan Window) HealthCheckLogs =>
        (HighPerformanceRateLimiter.RateLimitStrategy.FixedWindow, 1, TimeSpan.FromMinutes(5));
}