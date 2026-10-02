using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Diagnostics;
using Pragmatic.Caching.Extensions;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Caching.Samples.Samples;

/// <summary>
///     Observability: subscribe to the <see cref="ActivitySource" /> and <see cref="Meter" />
///     exposed by <see cref="CachingDiagnostics" /> (both named <c>"Pragmatic.Caching"</c>)
///     and observe activities + counter increments fire on real cache operations.
///     In production these are wired to OpenTelemetry exporters instead of console listeners.
/// </summary>
public static class ObservabilitySample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("8. Observability — ActivitySource + Meter listeners");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // ── Activity listener: prints each Cache.* activity as it stops ──────────────
        var activityCount = 0;
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CachingDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                activityCount++;
                var op = activity.GetTagItem(CacheTags.Operation);
                var key = activity.GetTagItem(CacheTags.Key);
                Console.WriteLine($"    [activity] {activity.OperationName} op={op} key={key}");
            }
        };
        ActivitySource.AddActivityListener(activityListener);

        // ── Meter listener: aggregates the cache counter instruments ─────────────────
        var counters = new Dictionary<string, long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CachingDiagnostics.SourceName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            counters.TryGetValue(instrument.Name, out var current);
            counters[instrument.Name] = current + measurement;
        });
        meterListener.Start();

        var services = new ServiceCollection();
        services.AddLogging();
#pragma warning disable EXTEXP0018 // HybridCache is experimental
        services.AddHybridCache();
#pragma warning restore EXTEXP0018
        services.AddPragmaticCaching();

        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<ICacheStack>();

        Console.WriteLine("  8.1 Drive real cache operations (miss, hit, invalidation)");
        Console.WriteLine("  ----------------------------------------------------------");

        ValueTask<string> Factory(CancellationToken _) => ValueTask.FromResult("payload");

        await cache.GetOrSetAsync("metrics:1", Factory); // miss → factory runs
        await cache.GetOrSetAsync("metrics:1", Factory); // hit  → factory skipped
        await cache.InvalidateByTagAsync("metrics");      // invalidation
        Console.WriteLine();

        Console.WriteLine("  8.2 Collected telemetry");
        Console.WriteLine("  -------------------------");
        Console.WriteLine($"    Activities observed: {activityCount}");
        Console.WriteLine($"    pragmatic.cache.hits          = {Total(counters, "pragmatic.cache.hits")}");
        Console.WriteLine($"    pragmatic.cache.misses        = {Total(counters, "pragmatic.cache.misses")}");
        Console.WriteLine($"    pragmatic.cache.invalidations = {Total(counters, "pragmatic.cache.invalidations")}");
        Console.WriteLine();
    }

    private static long Total(Dictionary<string, long> counters, string name)
        => counters.TryGetValue(name, out var value) ? value : 0;
}
