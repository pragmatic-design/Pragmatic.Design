using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;

namespace Pragmatic.Caching.Samples.Samples;

/// <summary>
///     Conditional caching with <see cref="CacheFactoryResult{T}" />: the factory decides
///     per-invocation whether the produced value should be stored. Use
///     <see cref="CacheFactoryResult{T}.Cache" /> to persist the value and
///     <see cref="CacheFactoryResult{T}.DoNotCache" /> to skip caching (e.g. null,
///     empty, or "not found" results) while still returning the value to the caller.
/// </summary>
public static class ConditionalCachingSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. Conditional Caching — CacheFactoryResult<T> (DoNotCache)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging();
#pragma warning disable EXTEXP0018 // HybridCache is experimental
        services.AddHybridCache();
#pragma warning restore EXTEXP0018
        services.AddPragmaticCaching();

        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<ICacheStack>();

        Console.WriteLine("  7.1 DoNotCache — empty result is NOT stored, factory re-runs");
        Console.WriteLine("  -------------------------------------------------------------");

        var emptyFactoryCalls = 0;

        // The factory returns DoNotCache for the "not found" case, so each call re-executes.
        ValueTask<CacheFactoryResult<string>> LookupMissing(CancellationToken _)
        {
            emptyFactoryCalls++;
            // Simulate "record not found" — do not pollute the cache with an empty value.
            return ValueTask.FromResult(CacheFactoryResult<string>.DoNotCache(string.Empty));
        }

        var miss1 = await cache.GetOrSetAsync("lookup:missing", LookupMissing);
        var miss2 = await cache.GetOrSetAsync("lookup:missing", LookupMissing);

        Console.WriteLine($"    Call 1: value=\"{miss1}\", factory calls so far: {emptyFactoryCalls}");
        Console.WriteLine($"    Call 2: value=\"{miss2}\", factory calls so far: {emptyFactoryCalls}");
        Console.WriteLine("    DoNotCache → value never stored → factory runs on every call.");
        Console.WriteLine();

        Console.WriteLine("  7.2 Cache — valid result IS stored, factory runs once");
        Console.WriteLine("  ------------------------------------------------------");

        var validFactoryCalls = 0;

        // The factory returns Cache for a real value, so it is stored and reused.
        ValueTask<CacheFactoryResult<string>> LookupExisting(CancellationToken _)
        {
            validFactoryCalls++;
            return ValueTask.FromResult(CacheFactoryResult<string>.Cache("Alice"));
        }

        var hit1 = await cache.GetOrSetAsync("lookup:42", LookupExisting);
        var hit2 = await cache.GetOrSetAsync("lookup:42", LookupExisting);

        Console.WriteLine($"    Call 1: value=\"{hit1}\", factory calls so far: {validFactoryCalls}");
        Console.WriteLine($"    Call 2: value=\"{hit2}\", factory calls so far: {validFactoryCalls} (cache hit!)");
        Console.WriteLine("    Cache → value stored → factory runs only once.");
        Console.WriteLine();
    }
}
