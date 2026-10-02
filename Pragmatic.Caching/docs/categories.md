# Category Routing

Category routing allows different subsystems to use isolated cache namespaces with per-category configuration. Each category is a strongly-typed marker class used as a generic type parameter.

---

## Why Categories

Without categories, all cache entries share a single `ICacheStack` instance. This creates three problems:

1. **Key collisions**: An output cache entry `user:42` and a permission cache entry `user:42` clash.
2. **Configuration bleed**: A 30-minute TTL appropriate for permission sets is too long for rate limiter counters.
3. **Invalidation noise**: Invalidating the `user:42` tag should not evict both output cache and permission cache entries when only permissions changed.

Categories solve this by giving each subsystem its own `ICacheStack` instance with a key prefix, independent default duration, and isolated tag namespace.

---

## Predefined Categories

`CacheCategories` is a static class in `Pragmatic.Abstractions` (namespace `Pragmatic.Caching`). Each nested sealed class is a marker type:

| Category | Marker Type | Consumer | Purpose |
|----------|-------------|----------|---------|
| **Default** | `CacheCategories.Default` | Business queries/actions | General-purpose cache (used when no category is specified) |
| **OutputCache** | `CacheCategories.OutputCache` | `PragmaticOutputCacheStore` | ASP.NET Core HTTP response caching |
| **RateLimiting** | `CacheCategories.RateLimiting` | `PragmaticDistributedRateLimiter` | Cross-instance rate limit counters |
| **Permissions** | `CacheCategories.Permissions` | `CachedPermissionResolver` | Cross-request permission set caching |
| **Configuration** | `CacheCategories.Configuration` | `ConfigurationResolver` | Remote configuration value caching |
| **Idempotency** | `CacheCategories.Idempotency` | `IdempotencyEndpointFilter` | Idempotency-key response replay for `[Idempotent]` endpoints |

These live in Abstractions (Layer 0) so any module can reference them without taking a dependency on `Pragmatic.Caching`.

---

## Custom Categories

Define your own categories as sealed marker classes:

```csharp
public static class AppCacheCategories
{
    public sealed class Analytics;
    public sealed class UserSessions;
    public sealed class Recommendations;
}
```

Then register them:

```csharp
builder.Services.AddPragmaticCaching(cache =>
{
    cache.ForCategory<AppCacheCategories.Analytics>(o =>
    {
        o.KeyPrefix = "analytics:";
        o.DefaultDuration = TimeSpan.FromHours(1);
    });
    cache.ForCategory<AppCacheCategories.UserSessions>(o =>
    {
        o.KeyPrefix = "sessions:";
        o.DefaultDuration = TimeSpan.FromMinutes(30);
    });
});
```

Use them on cacheable types:

```csharp
[Cacheable(Duration = "1h", Category = typeof(AppCacheCategories.Analytics))]
public partial class GetDashboardMetrics
{
    public required DateOnly Date { get; init; }
}
```

---

## CachingBuilder API

`AddPragmaticCaching(Action<CachingBuilder>)` exposes the builder for per-category routing:

```csharp
builder.Services.AddPragmaticCaching(cache =>
{
    // Configure defaults (applied to categories without specific config)
    cache.WithDefaultOptions(o =>
    {
        o.DefaultDuration = TimeSpan.FromMinutes(10);
        o.KeyPrefix = "myapp:";
    });

    // Register category-specific configuration
    cache.ForCategory<CacheCategories.OutputCache>(o => o.KeyPrefix = "oc:");
    cache.ForCategory<CacheCategories.Permissions>(o =>
    {
        o.KeyPrefix = "perms:";
        o.DefaultDuration = TimeSpan.FromMinutes(5);
    });
    cache.ForCategory<CacheCategories.RateLimiting>(o =>
    {
        o.KeyPrefix = "rl:";
        o.DefaultDuration = TimeSpan.FromMinutes(1);
    });
});
```

### CategoryCacheOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `KeyPrefix` | `string` | Auto-generated from type name | Prefix added to all keys and tags in this category |
| `DefaultDuration` | `TimeSpan?` | `null` (inherits global) | Default TTL for entries without explicit duration |

If `KeyPrefix` is not explicitly set, it auto-generates from the category type name in lowercase (e.g., `OutputCache` becomes `outputcache:`, `Permissions` becomes `permissions:`).

---

## CacheStackProvider Resolution Flow

`CacheStackProvider` is a static helper that resolves the correct `ICacheStack` for a given category:

```
CacheStackProvider.ForCategory<TCategory>(sp)
    |
    +--> sp.GetKeyedService<ICacheStack>(typeof(TCategory).FullName)
    |        |
    |        +--> Found? Return keyed ICacheStack (PrefixedCacheStack)
    |
    +--> Fallback: sp.GetRequiredService<ICacheStack>() (default HybridCacheStack)
```

Category-specific registrations use .NET 8+ keyed services. The service key is the full name of the marker type (`typeof(TCategory).FullName`).

```csharp
// Explicit resolution (bridges use this internally)
var permCache = CacheStackProvider.ForCategory<CacheCategories.Permissions>(serviceProvider);
await permCache.SetAsync("user:42", permissions, options, ct);

// Runtime Type-based resolution
var cache = CacheStackProvider.ForCategory(serviceProvider, categoryType);
```

### PrefixedCacheStack

Each registered category creates a `PrefixedCacheStack` (internal) that wraps the default `ICacheStack`. It:

- Prepends the `KeyPrefix` to all cache keys
- Prepends the `KeyPrefix` to all tags (for invalidation isolation)
- Applies the category `DefaultDuration` when no explicit duration is set on the entry

---

## Integration with Bridges

### Output Cache Bridge

`PragmaticOutputCacheStore` (in `Pragmatic.Endpoints.AspNetCore`) implements ASP.NET Core's `IOutputCacheStore`. It resolves `ICacheStack` via `CacheStackProvider.ForCategory<CacheCategories.OutputCache>()`, so HTTP response cache entries are isolated from business-layer cache entries.

Register with:

```csharp
services.UseOutputCacheFromPragmaticCaching();
```

### Rate Limiter Bridge

`PragmaticDistributedRateLimiter` (in `Pragmatic.Endpoints.AspNetCore`) implements `System.Threading.RateLimiting.RateLimiter`. It uses `ICacheStack` resolved via `CacheCategories.RateLimiting` for cross-instance fixed-window rate limit counters.

### Authorization Cache

`CachedPermissionResolver` (in `Pragmatic.Authorization`) optionally accepts an `ICacheStack` for cross-request permission caching. When `UsePermissionCache()` is configured, it caches resolved permission sets keyed by user ID (and tenant ID if multi-tenant).

---

## SG Auto-Detection

The source generator reads `[PragmaticMetadata(Caching, ...)]` assembly attributes to discover cache categories used across referenced modules. When categories are found, the host's generated `RegisterAllPragmaticServices()` method includes `ForCategory<T>()` registrations:

```csharp
// Auto-generated in PragmaticHost.g.cs
services.AddPragmaticCaching(cache =>
{
    // Auto-detected cache categories from [Cacheable(Category=...)] / [InvalidatesCache(Category=...)]
    cache.ForCategory<Pragmatic.Caching.CacheCategories.Permissions>(_ => { });
    cache.ForCategory<AppCacheCategories.Analytics>(_ => { });
});
```

This ensures that all categories used by referenced modules are registered even if the host does not configure them explicitly. The empty callback `(_ => { })` uses auto-generated defaults (key prefix from type name, no custom duration).

---

## Future: Backend Isolation Per-Category

The current implementation routes all categories through the same `HybridCacheStack` backend, differentiated only by key prefix. A future version may support per-category backend isolation (e.g., Redis for permissions, in-memory only for rate limiting). The `CachingBuilder.ForCategory<T>()` API is designed to support this extension without breaking changes.
