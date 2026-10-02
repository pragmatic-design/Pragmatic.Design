using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;

namespace Pragmatic.Caching.Samples.Samples;

/// <summary>
///     Live, end-to-end cache invalidation through the source-generated
///     <see cref="ICacheInvalidator" />. Event types marked <c>[InvalidatesCache]</c>
///     (see <see cref="UserUpdated" />, <see cref="OrderPlaced" />) get the
///     <see cref="ICacheInvalidator" /> interface added by the SG, with an
///     <see cref="ICacheInvalidator.InvalidateAsync" /> implementation generated from the
///     declared tags and keys. This sample seeds a real <see cref="ICacheStack" />, then calls
///     the generated invalidator to remove explicit keys and tag groups — no console-only text.
/// </summary>
public static class LiveInvalidationSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("9. Live Invalidation — generated ICacheInvalidator.InvalidateAsync");
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

        Console.WriteLine("  9.1 Explicit key removal via [InvalidatesCache(Keys = ...)]");
        Console.WriteLine("  ------------------------------------------------------------");

        // [InvalidatesCache("users", "tenant:{TenantId}", Keys = ["user:{UserId}"])] on UserUpdated.
        // The generated invalidator runs RemoveAsync("user:{UserId}") + InvalidateByTagAsync(...).
        const int userId = 123;
        const int tenantId = 42;
        await cache.SetAsync($"user:{userId}", "Alice (cached)");

        Console.WriteLine($"    Seeded:  user:{userId} = \"{await cache.GetAsync<string>($"user:{userId}")}\"");

        // The SG made UserUpdated implement ICacheInvalidator — call it directly.
        ICacheInvalidator userEvent = new UserUpdated { UserId = userId, TenantId = tenantId };
        await userEvent.InvalidateAsync(cache);

        var afterUser = await cache.GetAsync<string>($"user:{userId}");
        Console.WriteLine($"    After InvalidateAsync(): user:{userId} = \"{afterUser ?? "(null - removed)"}\"");
        Console.WriteLine();

        Console.WriteLine("  9.2 Multi-tag invalidation via [InvalidatesCache(\"orders\", \"customers\", ...)]");
        Console.WriteLine("  ----------------------------------------------------------------------------");

        // OrderPlaced invalidates the "orders", "customers" and "inventory" tag groups.
        await cache.SetAsync(
            "order:7",
            "Order #7",
            new CacheEntryOptions { Tags = ["orders"] });
        await cache.SetAsync(
            "customer:9:orders",
            "Customer 9 order list",
            new CacheEntryOptions { Tags = ["customers"] });

        Console.WriteLine("    Seeded:  order:7           (tag: orders)");
        Console.WriteLine("    Seeded:  customer:9:orders (tag: customers)");

        ICacheInvalidator orderEvent = new OrderPlaced { OrderId = 7, CustomerId = 9 };
        await orderEvent.InvalidateAsync(cache);

        Console.WriteLine("    Called OrderPlaced.InvalidateAsync() → tag groups invalidated.");
        Console.WriteLine($"    order:7           = \"{await cache.GetAsync<string>("order:7") ?? "(null - invalidated)"}\"");
        Console.WriteLine($"    customer:9:orders = \"{await cache.GetAsync<string>("customer:9:orders") ?? "(null - invalidated)"}\"");
        Console.WriteLine();

        Console.WriteLine("  Pipeline note: an event handler resolves ICacheStack from DI and calls");
        Console.WriteLine("  invalidator.InvalidateAsync(cache) after the mutation commits.");
        Console.WriteLine();
    }
}
