using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching;
using Pragmatic.Configuration.Extensions;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     Configuration read-caching via the in-process <c>ICacheStack</c> that
///     <see cref="ConfigurationServiceCollectionExtensions.AddPragmaticConfiguration"/> registers by
///     default. When <c>Pragmatic.Caching</c> is not present, an internal in-memory cache stack is used
///     as the fallback and the configuration store is wrapped with a read-through caching decorator.
/// </summary>
public static class CacheStackSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("9. Cache Stack — read-through configuration caching");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging();

        // EnableReadCaching is on by default: registers the fallback ICacheStack and
        // decorates IConfigurationStore with read-through caching.
        services.AddPragmaticConfiguration(options =>
        {
            options.EnableReadCaching = true;
        });

        using var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<ICacheStack>();

        Console.WriteLine($"  Registered ICacheStack: {cache.GetType().Name}");
        Console.WriteLine();

        // ── GetOrSetAsync: miss then hit ────────────────────────────────────────
        var factoryCalls = 0;
        var key = "config:smtp:host";

        var first = await cache.GetOrSetAsync(key, _ =>
        {
            factoryCalls++;
            return ValueTask.FromResult("mail.example.com");
        });
        Console.WriteLine($"  GetOrSetAsync #1 (miss): \"{first}\", factory calls = {factoryCalls}");

        var second = await cache.GetOrSetAsync(key, _ =>
        {
            factoryCalls++;
            return ValueTask.FromResult("should-not-run");
        });
        Console.WriteLine($"  GetOrSetAsync #2 (hit):  \"{second}\", factory calls = {factoryCalls} (factory not re-run)");
        Console.WriteLine();

        // ── Tag-based invalidation ──────────────────────────────────────────────
        await cache.SetAsync("config:a", "1", new CacheEntryOptions { Tags = ["config"] });
        await cache.SetAsync("config:b", "2", new CacheEntryOptions { Tags = ["config"] });

        Console.WriteLine($"  Before invalidation: config:a={await cache.GetAsync<string>("config:a")}, config:b={await cache.GetAsync<string>("config:b")}");
        await cache.InvalidateByTagAsync("config");
        Console.WriteLine($"  After InvalidateByTagAsync(\"config\"): config:a={await cache.GetAsync<string>("config:a") ?? "(null)"}, config:b={await cache.GetAsync<string>("config:b") ?? "(null)"}");
        Console.WriteLine();

        // ── Explicit removal ────────────────────────────────────────────────────
        await cache.RemoveAsync(key);
        Console.WriteLine($"  After RemoveAsync(\"{key}\"): {await cache.GetAsync<string>(key) ?? "(null - removed)"}");
        Console.WriteLine();
    }
}
