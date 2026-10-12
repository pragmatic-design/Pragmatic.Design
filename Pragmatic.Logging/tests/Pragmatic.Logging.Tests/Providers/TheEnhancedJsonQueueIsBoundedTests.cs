using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     <see cref="PragmaticEnhancedJsonProvider" /> holds at most <c>Performance.MaxQueueSize</c> entries, applies
///     <c>Performance.OverflowStrategy</c> past it, counts what it drops, and writes what it holds when disposed
///     (#144).
/// </summary>
/// <remarks>
///     The queue was an unbounded <c>ConcurrentQueue</c>: past <c>MaxAsyncQueueSize</c> every call started a
///     <c>Task.Run</c> flush that waited up to 100 ms for a semaphore and drained at most 100 entries, so under a
///     burst the queue kept growing with thousands of pending tasks behind it. The declared bound and overflow
///     strategy were read by nothing. Dispose drained one batch of 100, and dropped the rest without a count.
/// </remarks>
public class TheEnhancedJsonQueueIsBoundedTests : IDisposable
{
    private const int Bound = 1_000;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pragmatic-bounded-{Guid.NewGuid():N}.ndjson");

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    [Fact]
    public void ABurstOfTenThousand_HoldsAtMostTheBound_AndAccountsForEveryEntry()
    {
        const int burst = 10_000;
        long dropped;

        using (var provider = Provider(Pragmatic.Logging.Providers.QueueOverflowStrategy.DropOldest))
        {
            for (var i = 0; i < burst; i++)
                provider.WriteLog(Entry($"entry {i}"));

            Pending(provider).Should().BeLessThanOrEqualTo(Bound, "the queue never holds more than its declared bound");
            dropped = Dropped(provider);
        }

        var written = File.ReadLines(_path).Count();
        (written + dropped).Should().Be(burst, "every entry is written or counted as dropped, none vanishes");
    }

    [Fact]
    public void EntriesWithinTheBound_AreAllWrittenOnDispose()
    {
        using (var provider = Provider(Pragmatic.Logging.Providers.QueueOverflowStrategy.DropOldest))
        {
            for (var i = 0; i < Bound; i++)
                provider.WriteLog(Entry($"entry {i}"));
        }

        File.ReadLines(_path).Count().Should().Be(Bound, "dispose writes what the queue holds, not one batch of it");
    }

    private PragmaticEnhancedJsonProvider Provider(Pragmatic.Logging.Providers.QueueOverflowStrategy overflow)
    {
        var config = new PragmaticProviderConfiguration
        {
            MinimumLevel = LogLevel.Information,
            Performance = new PerformanceConfiguration
            {
                EnableBatching = true,
                MaxQueueSize = Bound,
                OverflowStrategy = overflow,
            },
        };
        config.CustomProperties["EnableNDJSON"] = true;
        config.CustomProperties["EnableAsyncBuffering"] = true;
        return new PragmaticEnhancedJsonProvider("bounded", config, _path);
    }

    private static int Pending(PragmaticEnhancedJsonProvider provider)
        => (int)provider.GetMetrics().CustomMetrics!["PendingLogCount"]!;

    private static long Dropped(PragmaticEnhancedJsonProvider provider)
        => provider.GetMetrics().CustomMetrics!.TryGetValue("DroppedLogCount", out var value) ? Convert.ToInt64(value) : -1;

    private static LogEntry Entry(string message) => new()
    {
        Timestamp = DateTime.UtcNow,
        LogLevel = LogLevel.Information,
        Category = "Burst",
        Message = message,
    };
}
