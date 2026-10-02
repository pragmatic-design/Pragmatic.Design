using System.Diagnostics;

namespace Pragmatic.Logging.Filtering;

/// <summary>
/// Sliding-window-log rate limiter: precise limiting of at most <c>maxMessages</c> within any
/// trailing <c>timeWindow</c>. Keeps the timestamps of admitted messages and evicts those that
/// have aged out of the window. Uses the monotonic clock (<see cref="Stopwatch.GetTimestamp"/>) —
/// an NTP step of the wall clock must not freeze or burst the limiter. Thread-safe.
/// </summary>
internal sealed class SlidingWindowRateLimiter(int maxMessages, TimeSpan timeWindow) : IRateLimiterStrategy
{
    private readonly Queue<long> _timestamps = new();
    private readonly Lock _lock = new();

    public bool ShouldAllow()
    {
        var now = Stopwatch.GetTimestamp();

        lock (_lock)
        {
            while (_timestamps.Count > 0 && Stopwatch.GetElapsedTime(_timestamps.Peek(), now) > timeWindow)
            {
                _timestamps.Dequeue();
            }

            if (_timestamps.Count < maxMessages)
            {
                _timestamps.Enqueue(now);
                return true;
            }

            return false;
        }
    }
}
