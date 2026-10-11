using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     What the cache stack and the invalidation runner log, as a provider receives it: id, name, level,
///     message, the state's pairs and the exception. The values are those Microsoft's generator produced for
///     these methods, so moving them to Pragmatic's call sites (<c>PragmaticLogCallSites</c>) changes none of
///     them for whoever filters, alerts or queries on them.
/// </summary>
public sealed class TheCacheStackKeepsItsLogEventsTests
{
    [Fact]
    public async Task EveryOperationOfTheStack_LogsAsMicrosoftsGeneratorDid()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddHybridCache();
        await using var provider = services.BuildServiceProvider();
        var stack = new HybridCacheStack(
            new TagRefusingHybridCache(provider.GetRequiredService<HybridCache>(), refusedTag: "broken"),
            new Logger<HybridCacheStack>(recorder));

        await stack.GetOrSetAsync("orders:42", _ => ValueTask.FromResult(42));
        await stack.TryGetAsync<int>("orders:42");
        await stack.TryGetAsync<int>("orders:43");
        await stack.SetAsync("orders:44", 44);
        await stack.IncrementAsync("hits", 5);
        await stack.RemoveAsync("orders:44");
        await stack.InvalidateByTagAsync("orders");
        var partial = () => stack.InvalidateByTagsAsync(["orders", "broken"]).AsTask();
        await partial.Should().ThrowAsync<AggregateException>();

        var entries = recorder.Entries.ToArray();

        AssertKeyed(entries, "LogGetOrSet", 459676850, LogLevel.Debug, "Cache get-or-set key='orders:42'", "key");
        AssertKeyed(entries, "LogCacheHit", 15025942, LogLevel.Debug, "Cache hit key='orders:42'", "key");
        AssertKeyed(entries, "LogCacheMiss", 666486765, LogLevel.Debug, "Cache miss key='orders:43', executing factory", "key");
        AssertKeyed(entries, "LogCacheSet", 454249657, LogLevel.Debug, "Cache set key='orders:44'", "key");
        AssertKeyed(entries, "LogCacheRemove", 179931983, LogLevel.Debug, "Cache remove key='orders:44'", "key");
        AssertKeyed(entries, "LogInvalidateTag", 1290605764, LogLevel.Information, "Cache invalidate tag='orders'", "tag");

        var increment = AssertKeyed(entries, "LogCacheIncrement", 1685466642, LogLevel.Debug,
            "Cache increment key='hits' delta=5 new=5", "key", "delta", "newValue");
        increment.Pairs[1].Value.Should().BeOfType<long>();
        increment.Pairs[2].Value.Should().BeOfType<long>();

        var tags = AssertKeyed(entries, "LogInvalidateTags", 801661931, LogLevel.Information, "Cache invalidating 2 tag(s)", "tagCount");
        tags.Pairs[0].Value.Should().BeOfType<int>();

        var failed = AssertKeyed(entries, "LogTagInvalidationFailed", 1028298710, LogLevel.Warning,
            "Cache tag invalidation failed for tag='broken'", "tag");
        failed.Exception.Should().BeOfType<InvalidOperationException>();
        failed.Exception!.Message.Should().Be("The distributed tier refused tag 'broken'");
    }

    [Fact]
    public async Task AnInvalidationWithNoStack_LogsAsMicrosoftsGeneratorDid()
    {
        var recorder = new RecordingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(recorder);
        await using var provider = services.BuildServiceProvider();

        await CacheInvalidationRunner.InvalidateAsync(provider, null, ["orders"], ["orders:1", "orders:2"]);

        var skipped = AssertKeyed(recorder.Entries.ToArray(), "LogInvalidationSkipped", 142470187, LogLevel.Warning,
            "A domain event declared 3 cache invalidation(s) via [InvalidatesCache] but no ICacheStack is registered — skipped, stale entries may be served",
            "entryCount");
        skipped.Pairs[0].Value.Should().BeOfType<int>();
    }

    private static RecordedLogEntry AssertKeyed(
        RecordedLogEntry[] entries, string name, int id, LogLevel level, string message, params string[] keys)
    {
        var entry = entries.First(e => e.EventId.Name == name);
        entry.EventId.Id.Should().Be(id);
        entry.Level.Should().Be(level);
        entry.Message.Should().Be(message);
        entry.Pairs.Select(p => p.Key).Should().Equal([.. keys, "{OriginalFormat}"]);
        return entry;
    }
}
