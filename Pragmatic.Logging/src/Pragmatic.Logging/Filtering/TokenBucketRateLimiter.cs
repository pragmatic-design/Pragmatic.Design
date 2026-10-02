using System.Diagnostics;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// Token bucket rate limiter: allows short bursts up to the bucket capacity while
/// maintaining the configured long-run average rate. Tokens refill continuously based
/// on elapsed time from the monotonic clock (<see cref="Stopwatch.GetTimestamp"/>) —
/// an NTP step of the wall clock must not starve or over-fill the bucket.
/// Thread-safe and allocation-free on the hot path.
/// </summary>
internal sealed class TokenBucketRateLimiter : IRateLimiterStrategy
{
    private readonly double _capacity;
    private readonly double _refillTokensPerTick;
    private readonly Lock _lock = new();

    private double _availableTokens;
    private long _lastRefillTimestamp;

    /// <param name="maxMessages">Bucket capacity (max burst) and the number of tokens granted per window.</param>
    /// <param name="timeWindow">The window over which <paramref name="maxMessages"/> tokens are replenished.</param>
    public TokenBucketRateLimiter(int maxMessages, TimeSpan timeWindow)
    {
        _capacity = maxMessages <= 0 ? 0 : maxMessages;

        // Tokens regenerated per Stopwatch tick so that a full window restores the full capacity.
        var windowTicks = timeWindow.TotalSeconds <= 0 ? 1 : timeWindow.TotalSeconds * Stopwatch.Frequency;
        _refillTokensPerTick = _capacity / windowTicks;

        _availableTokens = _capacity;
        _lastRefillTimestamp = Stopwatch.GetTimestamp();
    }

    public bool ShouldAllow()
    {
        lock (_lock)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = now - _lastRefillTimestamp;
            if (elapsed > 0)
            {
                _availableTokens = Math.Min(_capacity, _availableTokens + elapsed * _refillTokensPerTick);
                _lastRefillTimestamp = now;
            }

            if (_availableTokens >= 1.0)
            {
                _availableTokens -= 1.0;
                return true;
            }

            return false;
        }
    }
}
