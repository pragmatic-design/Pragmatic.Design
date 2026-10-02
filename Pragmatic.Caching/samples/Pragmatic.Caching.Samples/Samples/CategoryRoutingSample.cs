using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;

namespace Pragmatic.Caching.Samples.Samples;

/// <summary>
///     Category routing: register per-category cache stacks via the
///     <see cref="CachingBuilder.ForCategory{TCategory}" /> overload, then resolve the
///     correct <see cref="ICacheStack" /> at runtime through
///     <see cref="CacheStackProvider.ForCategory{TCategory}" />. Each category is backed by
///     its own keyed <see cref="PrefixedCacheStack" /> (distinct key prefix + default duration),
///     so identical logical keys never collide across categories.
/// </summary>
public static class CategoryRoutingSample
{
    // Marker types used purely to identify a cache category. No members required.
    public sealed class UserCache;

    public sealed class ProductCache;

    public static async Task RunAsync()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("6. Category Routing — ForCategory<T>() + CacheStackProvider");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddLogging();
#pragma warning disable EXTEXP0018 // HybridCache is experimental
        services.AddHybridCache();
#pragma warning restore EXTEXP0018

        // Register two categories, each with its own key prefix and default duration.
        // This uses the Action<CachingBuilder> overload of AddPragmaticCaching.
        services.AddPragmaticCaching(cache =>
        {
            cache.WithDefaultOptions(o => o.DefaultDuration = TimeSpan.FromMinutes(10));

            cache.ForCategory<UserCache>(o =>
            {
                o.KeyPrefix = "users:";
                o.DefaultDuration = TimeSpan.FromMinutes(5);
            });

            cache.ForCategory<ProductCache>(o =>
            {
                o.KeyPrefix = "products:";
                o.DefaultDuration = TimeSpan.FromHours(1);
            });
        });

        using var sp = services.BuildServiceProvider();

        Console.WriteLine("  6.1 Resolve a category-specific stack");
        Console.WriteLine("  ----------------------------------------");

        // CacheStackProvider is a static helper over keyed DI (key = typeof(T).FullName).
        ICacheStack userStack = CacheStackProvider.ForCategory<UserCache>(sp);
        ICacheStack productStack = CacheStackProvider.ForCategory<ProductCache>(sp);

        Console.WriteLine($"    UserCache    → {userStack.GetType().Name}");
        Console.WriteLine($"    ProductCache → {productStack.GetType().Name}");
        Console.WriteLine($"    Distinct instances: {!ReferenceEquals(userStack, productStack)}");
        Console.WriteLine();

        Console.WriteLine("  6.2 Prefixes keep identical keys isolated per category");
        Console.WriteLine("  --------------------------------------------------------");

        // Same logical key "42" stored in both categories — prefixes prevent collision.
        await userStack.SetAsync("42", "Alice (user)");
        await productStack.SetAsync("42", "Widget (product)");

        var user = await userStack.GetAsync<string>("42");
        var product = await productStack.GetAsync<string>("42");

        Console.WriteLine($"    userStack.Get(\"42\")    = \"{user}\"");
        Console.WriteLine($"    productStack.Get(\"42\") = \"{product}\"");
        Console.WriteLine("    Same key, different stacks → no collision (prefixes differ).");
        Console.WriteLine();

        Console.WriteLine("  6.3 Unregistered category falls back to the default stack");
        Console.WriteLine("  ----------------------------------------------------------");

        // No ForCategory<string>() was registered → returns the default ICacheStack.
        ICacheStack fallback = CacheStackProvider.ForCategory<string>(sp);
        Console.WriteLine($"    ForCategory<string>() → {fallback.GetType().Name} (default fallback)");
        Console.WriteLine();
    }
}
