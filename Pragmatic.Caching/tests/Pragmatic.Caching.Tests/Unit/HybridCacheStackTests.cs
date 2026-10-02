using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

public class HybridCacheStackTests : IDisposable
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private readonly ServiceProvider _provider;
    private readonly HybridCacheStack _sut;

    public HybridCacheStackTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        var services = new ServiceCollection();
        services.AddHybridCache();
        _provider = services.BuildServiceProvider();
        var hybridCache = _provider.GetRequiredService<HybridCache>();
        _sut = new HybridCacheStack(hybridCache);
    }

    public void Dispose() => _provider.Dispose();

    // --- Constructor ---

    [Fact]
    public void Constructor_NullCache_Throws()
    {
        var act = () => new HybridCacheStack(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- GetOrSetAsync ---

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_CallsFactory()
    {
        var factoryCalled = false;

        var result = await _sut.GetOrSetAsync<string>(
            "key1",
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult("value1");
            });

        factoryCalled.Should().BeTrue();
        result.Should().Be("value1");
    }

    [Fact]
    public async Task GetOrSetAsync_CacheHit_DoesNotCallFactory()
    {
        await _sut.SetAsync("key2", "cached");
        var factoryCalled = false;

        var result = await _sut.GetOrSetAsync<string>(
            "key2",
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult("new-value");
            });

        factoryCalled.Should().BeFalse();
        result.Should().Be("cached");
    }

    [Fact]
    public async Task GetOrSetAsync_NullKey_Throws()
    {
        var act = () => _sut.GetOrSetAsync<string>(
            null!, _ => ValueTask.FromResult("v")).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrSetAsync_WhitespaceKey_Throws()
    {
        var act = () => _sut.GetOrSetAsync<string>(
            "   ", _ => ValueTask.FromResult("v")).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrSetAsync_NullFactory_Throws()
    {
        var act = () => _sut.GetOrSetAsync<string>(
            "key", (Func<CancellationToken, ValueTask<string>>)null!).AsTask();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetOrSetAsync_WithOptions_UsesOptions()
    {
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(10),
            Tags = ["group-a"]
        };

        var result = await _sut.GetOrSetAsync<int>(
            "key-opts",
            _ => ValueTask.FromResult(42),
            options);

        result.Should().Be(42);
    }

    [Fact]
    public async Task GetOrSetAsync_WithNullOptions_Succeeds()
    {
        var result = await _sut.GetOrSetAsync<int>(
            "key-null-opts",
            _ => ValueTask.FromResult(99),
            null);

        result.Should().Be(99);
    }

    // --- GetAsync ---

    [Fact]
    public async Task GetAsync_CacheMiss_ReturnsDefault()
    {
        var result = await _sut.GetAsync<string>("missing-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_CacheHit_ReturnsCachedValue()
    {
        await _sut.SetAsync("get-key", "hello");

        var result = await _sut.GetAsync<string>("get-key");

        result.Should().Be("hello");
    }

    [Fact]
    public async Task GetAsync_NullKey_Throws()
    {
        var act = () => _sut.GetAsync<string>(null!).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAsync_ValueType_CacheMiss_ReturnsDefault()
    {
        var result = await _sut.GetAsync<int>("missing-int");

        result.Should().Be(0);
    }

    // --- TryGetAsync ---

    [Fact]
    public async Task TryGetAsync_CacheMiss_ReturnsFalse()
    {
        var (found, value) = await _sut.TryGetAsync<string>("try-missing");

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public async Task TryGetAsync_CacheHit_ReturnsTrueWithValue()
    {
        await _sut.SetAsync("try-hit", "present");

        var (found, value) = await _sut.TryGetAsync<string>("try-hit");

        found.Should().BeTrue();
        value.Should().Be("present");
    }

    [Fact]
    public async Task TryGetAsync_NullKey_Throws()
    {
        var act = () => _sut.TryGetAsync<string>(null!).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // --- SetAsync ---

    [Fact]
    public async Task SetAsync_StoresValue()
    {
        await _sut.SetAsync("set-key", 42);

        var result = await _sut.GetAsync<int>("set-key");
        result.Should().Be(42);
    }

    [Fact]
    public async Task SetAsync_NullKey_Throws()
    {
        var act = () => _sut.SetAsync(null!, "v").AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetAsync_WithOptions_Succeeds()
    {
        var options = CacheEntryOptions.WithDuration(TimeSpan.FromHours(1));

        await _sut.SetAsync("set-opts", "value", options);

        var result = await _sut.GetAsync<string>("set-opts");
        result.Should().Be("value");
    }

    [Fact]
    public async Task SetAsync_WithTags_Succeeds()
    {
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            Tags = ["tag-a", "tag-b"]
        };

        await _sut.SetAsync("tagged-key", "tagged-value", options);

        var result = await _sut.GetAsync<string>("tagged-key");
        result.Should().Be("tagged-value");
    }

    // --- RemoveAsync ---

    [Fact]
    public async Task RemoveAsync_ExistingKey_RemovesEntry()
    {
        await _sut.SetAsync("remove-key", "to-remove");

        await _sut.RemoveAsync("remove-key");

        var result = await _sut.GetAsync<string>("remove-key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_NullKey_Throws()
    {
        var act = () => _sut.RemoveAsync(null!).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RemoveAsync_NonExistingKey_DoesNotThrow()
    {
        var act = () => _sut.RemoveAsync("non-existing").AsTask();

        await act.Should().NotThrowAsync();
    }

    // --- InvalidateByTagAsync ---

    [Fact]
    public async Task InvalidateByTagAsync_NullTag_Throws()
    {
        var act = () => _sut.InvalidateByTagAsync(null!).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InvalidateByTagAsync_WhitespaceTag_Throws()
    {
        var act = () => _sut.InvalidateByTagAsync("  ").AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InvalidateByTagAsync_ValidTag_DoesNotThrow()
    {
        var act = () => _sut.InvalidateByTagAsync("some-tag").AsTask();

        await act.Should().NotThrowAsync();
    }

    // --- InvalidateByTagsAsync ---

    [Fact]
    public async Task InvalidateByTagsAsync_NullTags_Throws()
    {
        var act = () => _sut.InvalidateByTagsAsync(null!).AsTask();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_EmptyTags_DoesNotThrow()
    {
        var act = () => _sut.InvalidateByTagsAsync([]).AsTask();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_MultipleTags_DoesNotThrow()
    {
        var act = () => _sut.InvalidateByTagsAsync(["tag-1", "tag-2", "tag-3"]).AsTask();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_SkipsWhitespaceTags()
    {
        // Should not throw even with whitespace tags mixed in
        var act = () => _sut.InvalidateByTagsAsync(["valid", "  ", "", "also-valid"]).AsTask();

        await act.Should().NotThrowAsync();
    }

    // --- GetOrSetAsync with CacheFactoryResult (conditional caching) ---

    [Fact]
    public async Task GetOrSetAsync_CacheFactoryResult_Cache_StoresValue()
    {
        var result = await _sut.GetOrSetAsync<string>(
            "cond-cache",
            _ => ValueTask.FromResult(CacheFactoryResult<string>.Cache("stored")));

        result.Should().Be("stored");

        // Value should be in cache
        var (found, value) = await _sut.TryGetAsync<string>("cond-cache");
        found.Should().BeTrue();
        value.Should().Be("stored");
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFactoryResult_DoNotCache_ReturnsValueButDoesNotStore()
    {
        var result = await _sut.GetOrSetAsync<string>(
            "cond-no-cache",
            _ => ValueTask.FromResult(CacheFactoryResult<string>.DoNotCache("transient")));

        result.Should().Be("transient");

        // Value should NOT be in cache (was evicted after factory returned DoNotCache)
        var (found, _) = await _sut.TryGetAsync<string>("cond-no-cache");
        found.Should().BeFalse("DoNotCache should evict the value after returning it");
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFactoryResult_ImplicitConversion_Caches()
    {
        // Implicit conversion from T to CacheFactoryResult<T> defaults to Cache
        var result = await _sut.GetOrSetAsync<string>(
            "cond-implicit",
            _ => ValueTask.FromResult<CacheFactoryResult<string>>("implicitly-cached"));

        result.Should().Be("implicitly-cached");

        var (found, _) = await _sut.TryGetAsync<string>("cond-implicit");
        found.Should().BeTrue("implicit conversion should default to caching");
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFactoryResult_CacheHit_DoesNotCallFactory()
    {
        await _sut.SetAsync("cond-hit", "existing");
        var factoryCalled = false;

        var result = await _sut.GetOrSetAsync<string>(
            "cond-hit",
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult(CacheFactoryResult<string>.Cache("new"));
            });

        factoryCalled.Should().BeFalse();
        result.Should().Be("existing");
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFactoryResult_NullKey_Throws()
    {
        var act = () => _sut.GetOrSetAsync<string>(
            null!,
            _ => ValueTask.FromResult(CacheFactoryResult<string>.Cache("v"))).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFactoryResult_NullFactory_Throws()
    {
        var act = () => _sut.GetOrSetAsync<string>(
            "key",
            (Func<CancellationToken, ValueTask<CacheFactoryResult<string>>>)null!).AsTask();

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // --- IncrementAsync ---

    [Fact]
    public async Task IncrementAsync_FirstCall_TreatsMissingEntryAsZero()
    {
        var result = await _sut.IncrementAsync("inc-first", 1);

        result.Should().Be(1);
    }

    [Fact]
    public async Task IncrementAsync_SequentialCalls_Accumulate()
    {
        await _sut.IncrementAsync("inc-seq", 2);
        await _sut.IncrementAsync("inc-seq", 3);
        var result = await _sut.IncrementAsync("inc-seq", 5);

        result.Should().Be(10);
    }

    [Fact]
    public async Task IncrementAsync_NegativeDelta_Decrements()
    {
        await _sut.IncrementAsync("inc-neg", 10);
        var result = await _sut.IncrementAsync("inc-neg", -4);

        result.Should().Be(6);
    }

    [Fact]
    public async Task IncrementAsync_NullKey_Throws()
    {
        var act = () => _sut.IncrementAsync(null!, 1).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // Atomicity regression: the per-key lock must serialise concurrent read-modify-writes so
    // N parallel +1 increments on the same key sum to exactly N (a naive RMW would lose updates).
    [Fact]
    public async Task IncrementAsync_SequentialIncrements_SumExactly()
    {
        // Separates the two candidate causes of the parallel test's lost increments. With no
        // concurrency the per-key gate is never contended, so anything lost here is the read-modify-
        // write pair — a Set that the next Get does not see — and nothing to do with locking.
        const int count = 200;

        for (var i = 0; i < count; i++)
            await _sut.IncrementAsync("inc-sequential", 1);

        (await _sut.GetAsync<long>("inc-sequential")).Should().Be(count);
    }

    [Fact]
    public async Task IncrementAsync_ParallelIncrements_SumExactly()
    {
        const int concurrency = 200;
        var start = new TaskCompletionSource();

        // Each task keeps the value it was handed, in its own array slot: one write per task, no lock,
        // no allocation. IncrementAsync already returns what it wrote, so this observes the run without
        // instrumenting anything — earlier probes that used a queue or a decorator changed the outcome.
        var returned = new long[concurrency];

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            await start.Task.ConfigureAwait(false);
            returned[i] = await _sut.IncrementAsync("inc-parallel", 1).ConfigureAwait(false);
        }).ToArray();

        start.SetResult();
        await Task.WhenAll(tasks);

        var final = await _sut.GetAsync<long>("inc-parallel");

        // Which of the two causes it was, decided in the run that failed rather than in a separate one.
        // Two tasks handed the same value read the same current, so the gate did not serialise;
        // distinct values with a short final read means the writes were fine and the loss is under the
        // cache.
        var distinct = returned.Distinct().Count();
        _output.WriteLine($"distinct={distinct}/{concurrency} max={returned.Max()} final={final}");

        distinct.Should().Be(concurrency, "the gate must hand out each value once");
        returned.Max().Should().Be(concurrency, "the last increment must see all the others");
        final.Should().Be(concurrency);
    }
}
