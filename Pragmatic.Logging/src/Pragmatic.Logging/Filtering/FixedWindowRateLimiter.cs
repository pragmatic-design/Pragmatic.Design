using System.Diagnostics;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// Fixed-window rate limiter: a simple counter that resets at each window boundary.
/// Fastest strategy with the smallest memory footprint, at the cost of allowing up to
/// 2x the rate across a window boundary. Uses the monotonic clock — an NTP step of the
/// wall clock must not freeze or burst the limiter. Thread-safe.
/// </summary>
internal sealed class FixedWindowRateLimiter(int maxMessages, TimeSpan timeWindow) : IRateLimiterStrategy
{
    private readonly Lock _lock = new();

    private long _windowStart = Stopwatch.GetTimestamp();
    private int _count;

    public bool ShouldAllow()
    {
        var now = Stopwatch.GetTimestamp();

        lock (_lock)
        {
            if (Stopwatch.GetElapsedTime(_windowStart, now) >= timeWindow)
            {
                _windowStart = now;
                _count = 0;
            }

            if (_count < maxMessages)
            {
                _count++;
                return true;
            }

            return false;
        }
    }
}
