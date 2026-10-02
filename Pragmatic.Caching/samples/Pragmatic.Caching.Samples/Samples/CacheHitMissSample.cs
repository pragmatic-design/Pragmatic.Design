using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;

namespace Pragmatic.Caching.Samples.Samples;

/// <summary>
///     Real ICacheStack usage: GetOrSetAsync, cache hit vs miss, tag invalidation.
///     Uses HybridCache with in-memory backend (no Redis needed for demo).
/// </summary>
public static class CacheHitMissSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. Real Cache — Hit, Miss, Invalidation via ICacheStack");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Setup DI with HybridCache (L1 memory) + Pragmatic.Caching
        var services = new ServiceCollection();
        services.AddLogging();
#pragma warning disable EXTEXP0018 // HybridCache is experimental
        services.AddHybridCache();
#pragma warning restore EXTEXP0018
        services.AddPragmaticCaching();

        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<ICacheStack>();

        await ShowGetOrSet(cache);
        await ShowTagInvalidation(cache);
        await ShowRemoveByKey(cache);

        Console.WriteLine();
    }

    private static async Task ShowGetOrSet(ICacheStack cache)
    {
        Console.WriteLine("  4.1 GetOrSetAsync — cache miss then hit");
        Console.WriteLine("  -------------------------------------------");

        var callCount = 0;
        var key = "user:42";

        // First call: cache miss → factory executes
        var result1 = await cache.GetOrSetAsync(key, async _ =>
        {
            callCount++;
            await Task.Delay(10); // Simulated DB call
            return new { Id = 42, Name = "Alice" };
        });
        Console.WriteLine($"    Call 1: Name=\"{result1?.Name}\", factory called: {callCount} time(s)");

        // Second call: cache hit → factory NOT called
        var result2 = await cache.GetOrSetAsync(key, async _ =>
        {
            callCount++;
            await Task.Delay(10);
            return new { Id = 42, Name = "Should not appear" };
        });
        Console.WriteLine($"    Call 2: Name=\"{result2?.Name}\", factory called: {callCount} time(s) (cache hit!)");
        Console.WriteLine();
    }

    private static async Task ShowTagInvalidation(ICacheStack cache)
    {
        Console.WriteLine("  4.2 Tag invalidation — conceptual pattern");
        Console.WriteLine("  ----------------------------------------------");

        Console.WriteLine("    InvalidateByTagAsync(\"products\") removes all entries tagged \"products\".");
        Console.WriteLine("    With distributed cache (Redis), this clears across all instances.");
        Console.WriteLine("    In-memory only mode: individual RemoveAsync per key.");
        Console.WriteLine();

        // Demonstrate RemoveAsync as the reliable path
        await cache.SetAsync("product:1", "Widget");
        await cache.SetAsync("product:2", "Gadget");

        Console.WriteLine($"    Before: product:1 = \"{await cache.GetAsync<string>("product:1")}\"");
        Console.WriteLine($"    Before: product:2 = \"{await cache.GetAsync<string>("product:2")}\"");

        await cache.RemoveAsync("product:1");
        await cache.RemoveAsync("product:2");

        Console.WriteLine($"    After remove: product:1 = \"{await cache.GetAsync<string>("product:1") ?? "(null)"}\"");
        Console.WriteLine($"    After remove: product:2 = \"{await cache.GetAsync<string>("product:2") ?? "(null)"}\"");
        Console.WriteLine();
    }

    private static async Task ShowRemoveByKey(ICacheStack cache)
    {
        Console.WriteLine("  4.3 RemoveAsync — remove specific key");
        Console.WriteLine("  -----------------------------------------");

        await cache.SetAsync("session:abc", "user-session-data");

        var before = await cache.GetAsync<string>("session:abc");
        Console.WriteLine($"    Before: \"{before}\"");

        await cache.RemoveAsync("session:abc");

        var after = await cache.GetAsync<string>("session:abc");
        Console.WriteLine($"    After RemoveAsync: \"{after ?? "(null - removed)"}\"");
        Console.WriteLine();
    }
}
