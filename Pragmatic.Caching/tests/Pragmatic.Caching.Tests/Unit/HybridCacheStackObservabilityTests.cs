using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Diagnostics;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Tests verifying that <see cref="HybridCacheStack"/> emits the expected
///     metric counters and tracing activities (observability wiring).
/// </summary>
public sealed class HybridCacheStackObservabilityTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly HybridCacheStack _sut;

    public HybridCacheStackObservabilityTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        _provider = services.BuildServiceProvider();
        var hybridCache = _provider.GetRequiredService<HybridCache>();
        _sut = new HybridCacheStack(hybridCache);
    }

    public void Dispose() => _provider.Dispose();

    // --- Metric counters ---

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_IncrementsMissCounter()
    {
        using var counters = new CounterCollector();

        await _sut.GetOrSetAsync<string>("obs-miss", _ => ValueTask.FromResult("v"));

        counters.Total("pragmatic.cache.misses").Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheHit_IncrementsHitCounter()
    {
        await _sut.SetAsync("obs-hit", "cached");

        using var counters = new CounterCollector();
        await _sut.GetOrSetAsync<string>("obs-hit", _ => ValueTask.FromResult("new"));

        counters.Total("pragmatic.cache.hits").Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SetAsync_IncrementsSetCounter()
    {
        using var counters = new CounterCollector();

        await _sut.SetAsync("obs-set", 42);

        counters.Total("pragmatic.cache.sets").Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task RemoveAsync_IncrementsInvalidationCounter()
    {
        using var counters = new CounterCollector();

        await _sut.RemoveAsync("obs-remove");

        counters.Total("pragmatic.cache.invalidations").Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task InvalidateByTagAsync_IncrementsInvalidationCounter()
    {
        using var counters = new CounterCollector();

        await _sut.InvalidateByTagAsync("obs-tag");

        counters.Total("pragmatic.cache.invalidations").Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task TryGetAsync_CacheMiss_IncrementsMissCounter()
    {
        using var counters = new CounterCollector();

        var (found, _) = await _sut.TryGetAsync<string>("obs-tryget-miss");

        found.Should().BeFalse();
        counters.Total("pragmatic.cache.misses").Should().BeGreaterThanOrEqualTo(1);
    }

    // --- Tracing activities ---

    [Fact]
    public async Task SetAsync_StartsActivity()
    {
        using var activities = new ActivityCollector();

        await _sut.SetAsync("obs-activity-set", "v");

        activities.Names.Should().Contain("Cache.Set");
    }

    [Fact]
    public async Task GetOrSetAsync_StartsActivity()
    {
        using var activities = new ActivityCollector();

        await _sut.GetOrSetAsync<string>("obs-activity-getset", _ => ValueTask.FromResult("v"));

        activities.Names.Should().Contain("Cache.GetOrSet");
    }

    [Fact]
    public async Task RemoveAsync_StartsActivity()
    {
        using var activities = new ActivityCollector();

        await _sut.RemoveAsync("obs-activity-remove");

        activities.Names.Should().Contain("Cache.Remove");
    }

    [Fact]
    public async Task InvalidateByTagAsync_StartsActivityWithTagTag()
    {
        using var activities = new ActivityCollector();

        await _sut.InvalidateByTagAsync("obs-activity-tag");

        var activity = activities.Started.FirstOrDefault(a => a.DisplayName == "Cache.InvalidateByTag");
        activity.Should().NotBeNull();
        activity!.GetTagItem("pragmatic.cache.tags").Should().Be("obs-activity-tag");
    }

    /// <summary>
    ///     Collects values for Pragmatic.Caching meter instruments during the test.
    /// </summary>
    private sealed class CounterCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Dictionary<string, long> _totals = new();
        private readonly object _gate = new();

        public CounterCollector()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CachingDiagnostics.SourceName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                _ = tags;
                _ = state;
                lock (_gate)
                {
                    _totals.TryGetValue(instrument.Name, out var current);
                    _totals[instrument.Name] = current + measurement;
                }
            });
            _listener.Start();
        }

        public long Total(string instrumentName)
        {
            lock (_gate)
            {
                return _totals.TryGetValue(instrumentName, out var value) ? value : 0;
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    ///     Collects activities started on the Pragmatic.Caching ActivitySource during the test.
    /// </summary>
    private sealed class ActivityCollector : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _started = new();
        private readonly object _gate = new();

        public ActivityCollector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == CachingDiagnostics.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity =>
                {
                    lock (_gate)
                    {
                        _started.Add(activity);
                    }
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyList<Activity> Started
        {
            get
            {
                lock (_gate)
                {
                    return _started.ToList();
                }
            }
        }

        public IEnumerable<string> Names => Started.Select(a => a.DisplayName);

        public void Dispose() => _listener.Dispose();
    }
}
