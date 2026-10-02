using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Samples.Samples;

/// <summary>
///     Rate limiting protects sinks from log spam. <see cref="HighPerformanceRateLimiter"/>
///     produces a <see cref="FilterResult"/> backed by one of three real algorithms:
///     <list type="bullet">
///       <item><c>TokenBucket</c> — tolerates short bursts up to capacity, then throttles to the average rate.</item>
///       <item><c>SlidingWindow</c> — at most N within any trailing window (precise).</item>
///       <item><c>FixedWindow</c> — cheap counter that resets at window boundaries.</item>
///     </list>
///     This sample evaluates each filter against a burst of synthetic entries and prints how
///     many were allowed vs throttled — fully in-process, no sink required.
/// </summary>
public static class RateLimitingSample
{
    public static void Run()
    {
        Console.WriteLine("\n--- Rate limiting strategies ---");

        const int burst = 20;
        var window = TimeSpan.FromMinutes(1);

        // Capacity 5 over a 1-minute window: only the first 5 of a 20-message burst pass.
        DemonstrateStrategy("TokenBucket ", HighPerformanceRateLimiter.RateLimitStrategy.TokenBucket, maxMessages: 5, window, burst);
        DemonstrateStrategy("SlidingWindow", HighPerformanceRateLimiter.RateLimitStrategy.SlidingWindow, maxMessages: 5, window, burst);
        DemonstrateStrategy("FixedWindow ", HighPerformanceRateLimiter.RateLimitStrategy.FixedWindow, maxMessages: 5, window, burst);

        // Library-provided presets tuned per log level.
        var (strategy, max, presetWindow) = LoggingRateLimitPresets.ErrorLogs;
        Console.WriteLine($"\nPreset ErrorLogs → {strategy}, max {max} per {presetWindow.TotalSeconds:0}s");
    }

    private static void DemonstrateStrategy(
        string label,
        HighPerformanceRateLimiter.RateLimitStrategy strategy,
        int maxMessages,
        TimeSpan window,
        int burst)
    {
        var filter = HighPerformanceRateLimiter.CreateRateLimitedFilter(strategy, maxMessages, window);
        var context = new LogFilterContext();

        var allowed = 0;
        for (var i = 0; i < burst; i++)
        {
            var entry = new LogEntry { Message = $"event #{i}" };
            if (filter.Evaluate(entry, context))
                allowed++;
        }

        Console.WriteLine($"  {label}: allowed {allowed}/{burst} (throttled {burst - allowed}) with capacity {maxMessages}");
    }
}
