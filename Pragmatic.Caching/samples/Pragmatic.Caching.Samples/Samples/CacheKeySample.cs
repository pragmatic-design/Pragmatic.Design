namespace Pragmatic.Caching.Samples.Samples;

/// <summary>
///     Auto-generated cache keys: basic, multi-property, custom names, exclusions.
/// </summary>
public static class CacheKeySample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("1. Cache Key Generation");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        Console.WriteLine("  1.1 Basic — single property key");
        Console.WriteLine("  ----------------------------------");
        var getUser = new GetUser { UserId = 123 };
        Console.WriteLine($"    GetUser {{ UserId=123 }}");
        Console.WriteLine($"    Key: \"{getUser.GetCacheKey()}\"");
        Console.WriteLine();

        Console.WriteLine("  1.2 Multi-property — with exclusion");
        Console.WriteLine("  --------------------------------------");
        var getOrders = new GetUserOrders { TenantId = 42, UserId = 123, PageSize = 50 };
        Console.WriteLine($"    GetUserOrders {{ TenantId=42, UserId=123, PageSize=50 }}");
        Console.WriteLine($"    Key: \"{getOrders.GetCacheKey()}\"");
        Console.WriteLine($"    Note: PageSize is [CacheKey(Exclude=true)] — not in key");
        Console.WriteLine();

        Console.WriteLine("  1.3 Custom names and ordering");
        Console.WriteLine("  --------------------------------");
        var getProduct = new GetProductDetails { StoreId = 1, ProductId = 999 };
        Console.WriteLine($"    GetProductDetails {{ StoreId=1, ProductId=999 }}");
        Console.WriteLine($"    Key: \"{getProduct.GetCacheKey()}\"");
        Console.WriteLine($"    Note: [CacheKey(Name=\"store\", Order=1)] and [CacheKey(Name=\"product\", Order=2)]");
        Console.WriteLine();

        Console.WriteLine("  1.4 Static helpers — create keys without instantiation");
        Console.WriteLine("  --------------------------------------------------------");
        Console.WriteLine($"    GetUserCacheKeys.Create(100):            \"{GetUserCacheKeys.Create(100)}\"");
        Console.WriteLine($"    GetUserCacheKeys.Create(200):            \"{GetUserCacheKeys.Create(200)}\"");
        Console.WriteLine($"    GetProductDetailsCacheKeys.Create(1,50): \"{GetProductDetailsCacheKeys.Create(1, 50)}\"");
        Console.WriteLine();
    }
}
