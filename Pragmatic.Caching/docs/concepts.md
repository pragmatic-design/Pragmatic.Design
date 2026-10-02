# Architecture and Core Concepts

This guide explains **why** Pragmatic.Caching exists, how its pieces fit together, and how to choose the right approach for each caching scenario. Read this before diving into the individual feature guides.

---

## The Problem

Caching is deceptively simple in demos and deceptively painful in production. The .NET ecosystem provides the building blocks -- `IMemoryCache`, `IDistributedCache`, `HybridCache` -- but leaves three critical concerns entirely to the developer.

### Problem 1: Magic Strings Everywhere

```csharp
public class ProductService(IDistributedCache cache, IProductRepository repo)
{
    public async Task<ProductDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var key = $"product:{id}";  // Magic string — no compile-time validation
        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null)
            return JsonSerializer.Deserialize<ProductDto>(cached);

        var product = await repo.GetByIdAsync(id, ct);
        if (product is null) return null;

        var dto = ProductDto.FromEntity(product);
        await cache.SetStringAsync(key,
            JsonSerializer.Serialize(dto),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
            ct);
        return dto;
    }
}
```

The cache key `"product:{id}"` is a magic string. Rename the entity from `Product` to `Catalog` and you must find-and-replace every occurrence across the codebase. Miss one, and you get stale data served from the old key forever. The 5-minute duration is hardcoded -- change it in one place, forget another.

### Problem 2: Invalidation is Ad-Hoc

```csharp
public class ProductCommandHandler(IDistributedCache cache)
{
    public async Task HandleProductUpdated(ProductUpdated evt, CancellationToken ct)
    {
        // Must manually know every key pattern that references this product
        await cache.RemoveAsync($"product:{evt.ProductId}", ct);
        await cache.RemoveAsync($"products:tenant:{evt.TenantId}", ct);
        await cache.RemoveAsync($"products:category:{evt.CategoryId}", ct);
        // Did we miss "products:search:*"? Probably.
    }
}
```

Every new query that caches product data requires updating every invalidation handler that touches products. This coupling grows linearly with the number of cached queries, and missed invalidations produce stale data bugs that are notoriously difficult to reproduce.

### Problem 3: No Isolation Between Subsystems

```csharp
// Permission cache: 5-minute TTL, sensitive data
await cache.SetStringAsync("user:42", serializedPermissions, fiveMinutes);

// Output cache: 30-second TTL, public data
await cache.SetStringAsync("user:42", serializedHtml, thirtySeconds);

// Rate limiter: 1-minute window, counter
await cache.SetStringAsync("user:42", "7", oneMinute);
```

Three subsystems using the same `"user:42"` key. The last write wins, corrupting the other two. Even with distinct key prefixes, invalidating `"user:42"` in the permission subsystem should not evict the output cache entry -- but with a shared backend, it does.

**The fundamental issue**: the .NET caching primitives handle storage and retrieval, but the developer must manually maintain key generation, invalidation correctness, and subsystem isolation. These are exactly the concerns that a source generator can automate.

---

## The Solution

Pragmatic.Caching inverts the model. You declare **what** to cache and **when** to invalidate using attributes, and the source generator produces the key generation, options construction, and invalidation methods at compile time.

The same product caching scenario:

```csharp
[Cacheable(Duration = "5m", Tags = ["products", "product:{ProductId}"])]
public partial class GetProductById
{
    public required int ProductId { get; init; }
}
```

The source generator reads this class and produces:
- `GetCacheKey()` -- returns `"GetProductById:ProductId=42"` (typed, no magic strings)
- `GetCacheOptions()` -- returns `CacheEntryOptions` with 5-minute absolute duration and the expanded tags `["products", "product:42"]`
- `CacheCategory` -- returns the category type if `Category` is specified
- `GetProductByIdCacheKeys.Create(productId)` -- static helper for external key construction

For invalidation:

```csharp
[InvalidatesCache("products", "product:{ProductId}")]
public partial class ProductUpdated
{
    public int ProductId { get; init; }
}
```

The generator produces `InvalidateAsync(ICacheStack cache, CancellationToken ct)` that expands `{ProductId}` to the actual value and calls `cache.InvalidateByTagAsync` for each tag. No manual tracking of key patterns.

For subsystem isolation, category routing gives each subsystem its own `ICacheStack` with a key prefix, independent default duration, and isolated tag namespace:

```csharp
builder.Services.AddPragmaticCaching(cache =>
{
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

No reflection at runtime. No magic strings. No manual invalidation wiring. The generated code is visible in your IDE under `obj/`, fully debuggable.

---

## How It Works: The Caching Lifecycle

Every cached value in Pragmatic.Caching flows through a deterministic lifecycle. The source generator produces the code for steps 1-3 at compile time based on your attributes.

```
1. Key Generation         [Cacheable] --> GetCacheKey()
                          SG builds key from type name + property values
                          "GetProductById:ProductId=42"

2. Options Construction   [Cacheable] --> GetCacheOptions()
                          Duration, tags (with placeholder expansion),
                          sliding vs absolute, priority
                          Static allocation when tags are constants

3. Cache Operation        ICacheStack.GetOrSetAsync(key, factory, options)
                          HybridCacheStack: L1 memory + L2 distributed
                          Stampede protection: one factory call per key

4. Tag-Based Invalidation [InvalidatesCache] --> InvalidateAsync()
                          SG expands placeholders, calls InvalidateByTagAsync
                          Per-category or broadcast across all categories

5. Category Routing       CacheStackProvider.ForCategory<T>(sp)
                          Resolves keyed ICacheStack for subsystem isolation
                          PrefixedCacheStack adds key/tag prefix + default TTL
```

Steps 1, 2, and 4 are fully source-generated. Steps 3 and 5 are runtime operations backed by `HybridCacheStack` and `PrefixedCacheStack`.

---

## ICacheStack: The Core Abstraction

`ICacheStack` is the unified caching interface, defined in `Pragmatic.Abstractions` (Layer 0). It has ten members that cover all caching scenarios:

```csharp
public interface ICacheStack
{
    // Get-or-set with stampede protection
    ValueTask<T> GetOrSetAsync<T>(string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default);

    // Get-or-set where the factory decides whether the result should be cached
    // (CacheFactoryResult<T>.Cache(...) vs .DoNotCache(...)) — see "Conditional Caching" below
    ValueTask<T> GetOrSetAsync<T>(string key,
        Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default);

    // Direct get (returns default on miss)
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default);

    // Try-get (distinguishes miss from cached null)
    ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default);

    // Explicit set
    ValueTask SetAsync<T>(string key, T value,
        CacheEntryOptions? options = null, CancellationToken ct = default);

    // Remove by key
    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    // Invalidate by tag(s)
    ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default);
    ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default);

    // Synonym for InvalidateByTagAsync, aligned with the Remove* naming family.
    // Default interface method — delegates to InvalidateByTagAsync.
    ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
        => InvalidateByTagAsync(tag, ct);

    // Atomic counter increment/decrement. Process-local atomicity only on the default
    // HybridCacheStack (per-key semaphore) — NOT cross-instance atomic unless a backend
    // with a native atomic increment (e.g. Redis INCR) overrides this method.
    ValueTask<long> IncrementAsync(string key, long delta,
        TimeSpan? ttl = null, CancellationToken ct = default);
}
```

### Why ICacheStack instead of IDistributedCache?

| Concern | `IDistributedCache` | `ICacheStack` |
|---------|---------------------|---------------|
| Stampede protection | None -- concurrent misses all call the factory | Built-in via `HybridCache.GetOrCreateAsync` |
| Tag invalidation | Not supported | `InvalidateByTagAsync`, `InvalidateByTagsAsync` |
| Typed values | `byte[]` only -- manual serialization | Generic `T` with automatic serialization |
| Hit/miss detection | Return `null` -- ambiguous for nullable types | `TryGetAsync` returns `(bool Found, T? Value)` |
| L1+L2 tiering | Separate `IMemoryCache` + `IDistributedCache` | Unified via `HybridCache` backend |
| Observability | None built-in | Activities, metrics, structured logging |

### HybridCacheStack

The default `ICacheStack` implementation. It wraps `Microsoft.Extensions.Caching.Hybrid.HybridCache`, providing:

- **L1 (in-memory)**: Fast reads, process-local. No serialization cost.
- **L2 (distributed)**: Entries shared across instances via Redis, SQL Server, or any `IDistributedCache` backend.
- **Stampede protection**: When 10 concurrent requests miss the cache for the same key, only one factory call executes. The other 9 await the result.

The tiering is transparent. `GetOrSetAsync` checks L1 first, then L2, and only calls the factory on a complete miss.

### More Than One Instance

⚠️ **L2 shares the entries, not the invalidations.** Each instance keeps its own L1 copy for the entry's whole `Duration` (`LocalCacheExpiration` is set to it).

- An invalidation — `RemoveAsync`, a tag, `[InvalidatesCache]` — clears the L1 of the instance that ran it, and the L2 entry.
- Every other instance goes on serving its own copy until it expires.

That was measured with two hosts on one Redis. A host started afterwards read the new value from the database, while the host that already held the entry answered the old one.

`Pragmatic.Caching.Redis` closes it:

```csharp
services.AddPragmaticCaching();
services.AddStackExchangeRedisCache(o => o.Configuration = redis);   // L2: shares the entries
services.AddRedisCacheInvalidationBroadcast(redis);                  // shares the invalidations
```

How the broadcast works:

- `BroadcastingCacheStack` decorates `ICacheStack`. It runs every invalidation locally, then publishes it on a Redis pub/sub channel (`RedisCacheInvalidationOptions.Channel`, default `pragmatic:cache:invalidations`).
- `CacheInvalidationSubscriber`, a hosted service, applies the other instances' messages to its own `HybridCache` and ignores its own.
- It applies beneath the decorator, so a received invalidation is not published again.

What it does not guarantee:

- **At most once.** Pub/sub keeps nothing, so an instance that misses a message is stale until its copy expires — what every instance was before.
- **A failed publish is logged, not thrown.** The local invalidation has already happened.
- **Only the decorated stack is broadcast.** A direct `HybridCache.RemoveAsync` is not.

A single instance needs none of this. Without the call, nothing changes: the stack does not look for a broadcast at runtime.

### Conditional Caching (`CacheFactoryResult<T>`)

The second `GetOrSetAsync<T>` overload accepts a factory that returns `CacheFactoryResult<T>` instead of a bare `T`. This lets the factory decide, per invocation, whether the value it just computed should be persisted to the cache -- useful for "not found" or empty results that you don't want to pollute the cache with:

```csharp
var user = await cache.GetOrSetAsync("user:404", async ct =>
{
    var found = await repo.FindAsync(userId, ct);
    return found is null
        ? CacheFactoryResult<UserDto?>.DoNotCache(null)   // computed, but not stored
        : CacheFactoryResult<UserDto?>.Cache(found);       // computed and stored
});
```

- `CacheFactoryResult<T>.Cache(value)` -- the value is returned to the caller *and* written to the cache, same as the plain `GetOrSetAsync` overload.
- `CacheFactoryResult<T>.DoNotCache(value)` -- the value is returned to the caller but never persisted; `HybridCacheStack` explicitly removes it from the cache after the factory runs (`ShouldCache == false`), so the factory re-executes on every subsequent call for that key until a `Cache(...)` result is produced.

### CacheEntryOptions

Every cache operation optionally accepts `CacheEntryOptions`:

```csharp
public sealed class CacheEntryOptions
{
    public TimeSpan? Duration { get; init; }          // Absolute expiration
    public TimeSpan? SlidingDuration { get; init; }   // See "Sliding is not access-refreshed" below
    public ImmutableArray<string> Tags { get; init; }  // Tags for group invalidation
    public CachePriority Priority { get; init; }       // Advisory only — see "CachePriority" below

    public static CacheEntryOptions Default { get; }  // 5-minute absolute duration
    public static CacheEntryOptions WithDuration(TimeSpan d);
    public static CacheEntryOptions WithSliding(TimeSpan d);
}
```

When the source generator produces `GetCacheOptions()`, it returns a `CacheEntryOptions` instance with the values from your `[Cacheable]` attribute. If all tags are constants (no `{Property}` placeholders), the generated code uses a `static readonly` field to avoid per-call allocation.

### Sliding is not access-refreshed on the default backend

`HybridCache` (the .NET BCL type `HybridCacheStack` wraps) has **no true sliding expiration**. `SlidingDuration` is approximated as the **L1 (local) cache's absolute expiration** -- a shorter, fixed per-tier TTL, not a window that resets every time the entry is read. Concretely, `HybridCacheStack.ToHybridCacheEntryOptions` maps `SlidingDuration` to `HybridCacheEntryOptions.LocalCacheExpiration`, which is an absolute bound from the write, unaffected by subsequent reads. An entry set with `Sliding = true` will still expire from L1 after that duration even under continuous traffic. There is no HybridCache-backed way to get a true access-refreshed window; if you need one, you would need a custom `ICacheStack` implementation.

### CachePriority

`Priority` is **advisory metadata only on the default backend**. `HybridCacheEntryOptions` has no priority concept, so `HybridCacheStack` never reads or applies `CacheEntryOptions.Priority` -- setting it has zero runtime effect unless you register a custom `ICacheStack` that chooses to honor it:

| Priority | Behavior (only if a custom `ICacheStack` implements it) |
|----------|-----------------------------------------------------------|
| `Low` | First to be evicted when memory pressure occurs |
| `Normal` | Default. Standard eviction behavior |
| `High` | Less likely to be evicted. Use for expensive-to-compute values |
| `NeverRemove` | Never auto-evicted. Only expires by duration. Use sparingly |

---

## Cache Attributes

Three attributes control the source generator's output.

### `[Cacheable]`

Marks a type for cache key and options generation. Applied to query classes, DomainActions, or standalone types.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Duration` | `string` | `"5m"` | Cache duration. Formats: `"30s"`, `"5m"`, `"1h"`, `"1d"`, or TimeSpan string `"00:15:00"` |
| `Tags` | `string[]?` | `null` | Tags for group invalidation. Supports `{Property}` placeholders |
| `Sliding` | `bool` | `false` | Maps to a shorter L1 absolute TTL on the default backend, **not** an access-refreshed window -- see [Sliding is not access-refreshed](#sliding-is-not-access-refreshed-on-the-default-backend) |
| `Priority` | `CachePriority` | `Normal` | Advisory only -- **not honored** by the default `HybridCacheStack`; see [CachePriority](#cachepriority) |
| `Category` | `Type?` | `null` | Cache category marker type for routing to a specific `ICacheStack` |

The type must be `partial` (diagnostic `PRAG1700` if not).

### `[CacheKey]`

Customizes how a property contributes to the generated cache key. Applied to individual properties within a `[Cacheable]` type.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | `string?` | `null` | Custom name in the key segment. Default: property name |
| `Exclude` | `bool` | `false` | When true, excludes this property from the key |
| `Order` | `int` | `int.MaxValue` | Key segment order. Lower values appear first |

**Default behavior**: All public properties are included in the cache key, ordered by declaration. Use `[CacheKey]` only when you need to customize.

### `[InvalidatesCache]`

Marks a type for cache invalidation generation. Applied to whatever writes the rows: a domain event, a mutation, or a `[DomainAction]`. The invalidation runs after that operation commits, so a failed one invalidates nothing.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Tags` | `string[]` | `[]` | Tags to invalidate. Supports `{Property}` placeholders. Empty = convention-based |
| `Keys` | `string[]?` | `null` | Explicit cache keys to remove |
| `Category` | `Type?` | `null` | Target category. `null` = broadcast to ALL categories |

Convention-based invalidation: when no tags are specified, the generator derives a tag from the event name. It strips a trailing `Event`, `Updated`, `Created`, `Deleted`, or `Changed` suffix (whichever matches first), then pluralizes and lowercases the remainder. For example, `ProductUpdated` strips `Updated` -> `Product` -> invalidates the `"products"` tag; `OrderCreated` strips `Created` -> `Order` -> invalidates the `"orders"` tag.

---

## Tag-Based Invalidation

Tags are the mechanism for invalidating groups of related cache entries without tracking individual keys.

### How Tags Work

When you cache a value with tags, those tags are recorded alongside the entry:

```csharp
[Cacheable(Duration = "10m", Tags = ["products", "tenant:{TenantId}"])]
public partial class GetProductsByTenant
{
    public required int TenantId { get; init; }
}
```

For `TenantId = 42`, the generated `GetCacheOptions()` expands the tags to `["products", "tenant:42"]`. When any invalidation targets the `"products"` tag or the `"tenant:42"` tag, this entry is evicted.

### Placeholder Expansion

Tag placeholders use the `{PropertyName}` syntax. The source generator validates at compile time that the referenced property exists on the type (diagnostic `PRAG1703` if not).

```csharp
// Cacheable side — tags stored with the entry
[Cacheable(Duration = "5m", Tags = ["users", "user:{UserId}", "tenant:{TenantId}"])]
public partial class GetUserProfile
{
    public required Guid UserId { get; init; }
    public required int TenantId { get; init; }
}

// Invalidation side — tags expanded and invalidated
[InvalidatesCache("user:{UserId}")]
public partial class UserProfileUpdated
{
    public Guid UserId { get; init; }
}
```

When `UserProfileUpdated.InvalidateAsync` runs with `UserId = abc-123`, it calls `cache.InvalidateByTagAsync("user:abc-123")`. Every cached entry tagged with `"user:abc-123"` is evicted, regardless of which query type created it.

### Broadcast vs Targeted Invalidation

By default, `[InvalidatesCache]` reaches every registered cache category: the entry could live in any of them, so the invalidation is issued against each stack in turn. With no categories configured that is a single call to the default stack. Set `Category` to target one subsystem instead:

```csharp
// Broadcast: invalidates across ALL categories
[InvalidatesCache("users:{UserId}")]
public partial class UserUpdated
{
    public Guid UserId { get; init; }
}

// Targeted: only invalidates within the Permissions category
[InvalidatesCache("user:{UserId}", Category = typeof(CacheCategories.Permissions))]
public partial class UserRolesChanged
{
    public Guid UserId { get; init; }
}
```

Use targeted invalidation when you know that only one subsystem's cache is affected. This avoids unnecessary evictions in other categories.

---

## Category Routing

Categories solve the subsystem isolation problem by giving each subsystem its own `ICacheStack` instance with a key prefix, independent default duration, and isolated tag namespace.

### Why Categories

Without categories, all cache entries share a single `ICacheStack`. This causes three problems:

1. **Key collisions**: An output cache entry `user:42` and a permission cache entry `user:42` overwrite each other.
2. **Configuration bleed**: A 30-minute TTL for permission sets is too long for rate limiter counters, but both share the same default duration.
3. **Invalidation noise**: Invalidating `user:42` for permissions should not evict the output cache entry.

Categories solve all three: each category's `PrefixedCacheStack` adds a prefix to all keys and tags, applies its own default duration, and creates an isolated namespace.

### Predefined Categories

Six categories are predefined in `Pragmatic.Abstractions` so any module can reference them without depending on `Pragmatic.Caching`:

| Category | Marker Type | Consumer | Purpose |
|----------|-------------|----------|---------|
| Default | `CacheCategories.Default` | Business queries/actions | General-purpose (used when no category is specified) |
| OutputCache | `CacheCategories.OutputCache` | `PragmaticOutputCacheStore` | ASP.NET Core HTTP response caching |
| RateLimiting | `CacheCategories.RateLimiting` | `PragmaticDistributedRateLimiter` | Cross-instance rate limit counters |
| Permissions | `CacheCategories.Permissions` | `CachedPermissionResolver` | Cross-request permission set caching |
| Configuration | `CacheCategories.Configuration` | `ConfigurationResolver` | Remote configuration value caching |
| Idempotency | `CacheCategories.Idempotency` | `IdempotencyEndpointFilter` | Idempotency-key response replay for `[Idempotent]` endpoints |

### Custom Categories

Define application-specific categories as sealed marker classes:

```csharp
public static class AppCacheCategories
{
    public sealed class Analytics;
    public sealed class UserSessions;
    public sealed class Recommendations;
}
```

Register them with per-category configuration:

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

### CacheStackProvider Resolution Flow

`CacheStackProvider` resolves the correct `ICacheStack` for a given category:

```
CacheStackProvider.ForCategory<TCategory>(sp)
    |
    +--> sp.GetKeyedService<ICacheStack>(typeof(TCategory).FullName)
    |        |
    |        +--> Found? Return keyed ICacheStack (PrefixedCacheStack)
    |
    +--> Fallback: sp.GetRequiredService<ICacheStack>() (default HybridCacheStack)
```

Category-specific registrations use .NET 8+ keyed services. The service key is `typeof(TCategory).FullName`.

### PrefixedCacheStack

Each registered category creates a `PrefixedCacheStack` (internal) that wraps the default `ICacheStack`. It:

1. Prepends `KeyPrefix` to all cache keys
2. Prepends `KeyPrefix` to all tags (for invalidation isolation)
3. Applies `DefaultDuration` when no explicit duration is set on the entry

Example: with prefix `"perms:"`, a key `"user:42"` becomes `"perms:user:42"`, and a tag `"user:42"` becomes `"perms:user:42"`. This guarantees that permission cache invalidation never touches entries in other categories.

### CategoryCacheOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `KeyPrefix` | `string` | Auto-generated from type name | Prefix for all keys and tags in this category |
| `DefaultDuration` | `TimeSpan?` | `null` (inherits global) | Default TTL for entries without explicit duration |

If `KeyPrefix` is not explicitly set, it auto-generates from the category type name in lowercase (e.g., `OutputCache` becomes `outputcache:`, `Permissions` becomes `permissions:`).

---

## ICacheable and ICacheInvalidator Interfaces

The source generator implements these interfaces on your types.

### ICacheable

Generated on types marked with `[Cacheable]`:

```csharp
public interface ICacheable
{
    string GetCacheKey();
    CacheEntryOptions GetCacheOptions();
    Type? CacheCategory => null;  // Overridden when Category is specified
}
```

This interface enables the runtime to work with any cacheable type generically -- for example, the DomainAction invoker can detect `ICacheable` on an action and automatically cache results.

### ICacheInvalidator

Generated on types marked with `[InvalidatesCache]`:

```csharp
public interface ICacheInvalidator
{
    ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default);
    Type? InvalidationCategory => null;  // null = broadcast to all categories
}
```

The `InvalidationCategory` default interface method returns `null`, meaning broadcast. When `Category` is specified on the attribute, the generated override returns `typeof(TCategory)`.

---

## What Gets Generated

For each type marked with caching attributes, the source generator produces one or more files. The exact output depends on the attribute and properties.

### For `[Cacheable]` types

| Generated Output | Content | Condition |
|------------------|---------|-----------|
| `{Type}.Cache.g.cs` | Partial class implementing `ICacheable`: `GetCacheKey()`, `GetCacheOptions()`, `CacheCategory` | Always |
| `{Type}CacheKeys` class | Static helper with `Create(...)` method for external key construction | Always (in same file) |

Example: for a `GetProductById` class with `ProductId` property:

```csharp
// Generated: GetProductById.Cache.g.cs
public partial class GetProductById : ICacheable
{
    private static readonly CacheEntryOptions __cacheOptions = new()
    {
        Duration = TimeSpan.FromSeconds(300),
        Tags = ["products"]
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetCacheKey() => $"GetProductById:ProductId={ProductId}";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CacheEntryOptions GetCacheOptions() => __cacheOptions;
}

public static partial class GetProductByIdCacheKeys
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Create(int productId) => $"GetProductById:ProductId={productId}";
}
```

When tags contain placeholders (e.g., `"product:{ProductId}"`), `GetCacheOptions()` cannot use a static field because tags vary per instance. The generated code allocates a new `CacheEntryOptions` per call:

```csharp
// When tags have placeholders — per-call allocation
public CacheEntryOptions GetCacheOptions() => new()
{
    Duration = TimeSpan.FromSeconds(300),
    Tags = ["products", $"product:{ProductId}"]
};
```

### For `[InvalidatesCache]` types

| Generated Output | Content | Condition |
|------------------|---------|-----------|
| `{Type}.CacheInvalidator.g.cs` | Partial class implementing `ICacheInvalidator`: `InvalidateAsync()`, `InvalidationCategory` | Always |

Example: for a `ProductUpdated` class:

```csharp
// Generated: ProductUpdated.CacheInvalidator.g.cs
public partial class ProductUpdated : ICacheInvalidator
{
    public async ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default)
    {
        await cache.InvalidateByTagAsync("products", ct).ConfigureAwait(false);
        await cache.InvalidateByTagAsync($"product:{this.ProductId}", ct).ConfigureAwait(false);
    }
}
```

When `Category` is specified, the generated `InvalidationCategory` property returns the category type, allowing the runtime to route the invalidation to the correct `ICacheStack`.

### Generated file location

All generated files live under `obj/Debug/net10.0/generated/` and are visible in the IDE under **Dependencies > Analyzers > Pragmatic.SourceGenerator**. You can set breakpoints in generated code.

---

## CachingBuilder and Service Registration

### Registration Overloads

`AddPragmaticCaching` has three overloads:

```csharp
// 1. Defaults only — no categories
services.AddPragmaticCaching();

// 2. Configure global options
services.AddPragmaticCaching(options =>
{
    options.DefaultDuration = TimeSpan.FromMinutes(10);
    // There is no global key prefix: use per-category KeyPrefix via ForCategory<T>().
});

// 3. Full builder — global options + per-category routing
services.AddPragmaticCaching(cache =>
{
    cache.WithDefaultOptions(o => o.DefaultDuration = TimeSpan.FromMinutes(10));
    cache.ForCategory<CacheCategories.Permissions>(o =>
    {
        o.KeyPrefix = "perms:";
        o.DefaultDuration = TimeSpan.FromMinutes(5);
    });
});
```

All overloads register `ICacheStack` as `HybridCacheStack` (singleton). The builder overload additionally registers keyed `ICacheStack` instances for each category.

### CachingOptions (Global)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultDuration` | `TimeSpan` | 5 minutes | Fallback expiration `HybridCacheStack` applies when a cache entry's `CacheEntryOptions` specifies no `Duration`. Since `[Cacheable]` always sets a `Duration` (default `"5m"`), this mainly affects direct `ICacheStack` usage with no/partial options |
| `EnableQueryCaching` | `bool` | `true` | Whether `[Cacheable]` queries are cached. `false` switches query caching off without changing registrations — the executor resolves no stack |
| `EnableEventInvalidation` | `bool` | `true` | Whether `[InvalidatesCache]` declarations run after the operation commits — a mutation or an action alike. `false` skips them |

### SG Auto-Detection

The source generator detects category usage from `[Cacheable(Category = typeof(...))]` and `[InvalidatesCache(Category = typeof(...))]` across referenced assemblies. In host mode (with Pragmatic.Composition), the generated `RegisterAllPragmaticServices()` method auto-registers `ForCategory<T>()` for every detected category:

```csharp
// Auto-generated in PragmaticHost.g.cs
services.AddPragmaticCaching(cache =>
{
    cache.ForCategory<CacheCategories.Permissions>(_ => { });
    cache.ForCategory<AppCacheCategories.Analytics>(_ => { });
});
```

The empty callback `(_ => { })` uses auto-generated defaults. The user can override these with explicit configuration in `Program.cs` (DI last-registration-wins).

---

## Observability

`HybridCacheStack` provides built-in OpenTelemetry integration for every cache operation.

### Distributed Tracing (Activities)

Every operation creates an `Activity` from `CachingDiagnostics.ActivitySource` (source name: `"Pragmatic.Caching"`):

| Activity Name | Operation | Tags |
|---------------|-----------|------|
| `Cache.GetOrSet` | Get-or-set with factory | `pragmatic.cache.key`, `pragmatic.cache.operation` |
| `Cache.TryGet` | Try-get with hit/miss detection | `pragmatic.cache.key`, `cache.hit` |
| `Cache.Set` | Explicit set | `pragmatic.cache.key` |
| `Cache.Remove` | Remove by key | `pragmatic.cache.key` |
| `Cache.InvalidateByTag` | Single tag invalidation | `pragmatic.cache.tags` |
| `Cache.InvalidateByTags` | Multi-tag invalidation | `pragmatic.cache.tags` |
| `Cache.RemoteInvalidation` | An invalidation another instance broadcast, applied here (`Pragmatic.Caching.Redis`) | `pragmatic.cache.tags` or `pragmatic.cache.key`, `pragmatic.cache.node` |

### Metrics (Counters)

Four counters are exposed via `System.Diagnostics.Metrics` (meter name: `"Pragmatic.Caching"`):

| Metric | Type | Description |
|--------|------|-------------|
| `pragmatic.cache.hits` | Counter | Total cache hits (from `TryGetAsync`) |
| `pragmatic.cache.misses` | Counter | Total cache misses (from `TryGetAsync`) |
| `pragmatic.cache.sets` | Counter | Total explicit set operations |
| `pragmatic.cache.invalidations` | Counter | Total invalidations (remove + tag) |

### Structured Logging

`HybridCacheStack` uses `[LoggerMessage]` source-generated partial methods for zero-allocation structured logging:

| Level | Message |
|-------|---------|
| Debug | `Cache get-or-set key='{key}'` |
| Debug | `Cache hit key='{key}'` |
| Debug | `Cache miss key='{key}', executing factory` |
| Debug | `Cache set key='{key}'` |
| Debug | `Cache remove key='{key}'` |
| Information | `Cache invalidate tag='{tag}'` |
| Information | `Cache invalidating {tagCount} tag(s)` |
| Warning | `Cache tag invalidation failed for tag='{tag}'` |

---

## Ecosystem Integration Overview

Pragmatic.Caching is a cross-cutting concern that integrates with several other Pragmatic modules. Each integration is opt-in: reference the package, and the SG or runtime detects it.

### Actions

When a DomainAction or Query class is marked with `[Cacheable]`, the invoker pipeline can automatically cache results. The `ICacheable` interface on the action tells the invoker to call `GetCacheKey()` and `GetCacheOptions()` before executing the handler, and to cache the result on a successful response.

### Persistence

Query results can be cached with entity-tag invalidation. When a mutation modifies an entity, the generated `[InvalidatesCache]` handler on the corresponding domain event clears all cached queries tagged with that entity type.

### Events

Domain events marked with `[InvalidatesCache]` integrate with the event dispatcher. When the event fires, the generated `InvalidateAsync` method is called automatically, clearing stale cache entries.

### Endpoints (ASP.NET Core Bridges)

Two bridges connect Pragmatic.Caching to ASP.NET Core infrastructure:

- **Output Cache Bridge**: `PragmaticOutputCacheStore` implements `IOutputCacheStore`, routing HTTP response caching through `ICacheStack` via `CacheCategories.OutputCache`. Register with `services.UseOutputCacheFromPragmaticCaching()`.
- **Rate Limiter Bridge**: `PragmaticDistributedRateLimiter` implements `RateLimiter`, using `ICacheStack` via `CacheCategories.RateLimiting` for cross-instance fixed-window rate limit counters.

### Authorization

`CachedPermissionResolver` optionally accepts `ICacheStack` for cross-request permission caching. When `UsePermissionCache()` is configured, resolved permission sets are cached per user (and per tenant if multi-tenant), using `CacheCategories.Permissions` for isolation.

### Configuration

Remote configuration values are cached via `ICacheStack` using `CacheCategories.Configuration`. This replaces the former `IConfigurationCache` interface with a unified caching approach.

---

## Choosing the Right Approach

| Scenario | Approach |
|----------|----------|
| Cache a query/action result with typed key | `[Cacheable]` + `ICacheStack.GetOrSetAsync` |
| Invalidate on domain events | `[InvalidatesCache]` on the event class |
| Manual cache with custom logic | Inject `ICacheStack` directly, call `SetAsync`/`RemoveAsync` |
| Different TTLs for different subsystems | Category routing with `ForCategory<T>()` |
| Shorter L1 TTL for hot, rarely-changing data (**not** true access-refreshed sliding) | `[Cacheable(Sliding = true)]` -- see [Sliding is not access-refreshed](#sliding-is-not-access-refreshed-on-the-default-backend) |
| Compute a value but skip caching it (e.g. "not found") | `ICacheStack.GetOrSetAsync` overload returning `CacheFactoryResult<T>.DoNotCache(...)` -- see [Conditional Caching](#conditional-caching-cachefactoryresultt) |
| Keep value under memory pressure | Advisory only, **no-op on the default backend** -- `[Cacheable(Priority = CachePriority.High)]` only has effect with a custom `ICacheStack` |
| Multi-tenant key isolation | Per-category `KeyPrefix` via `ForCategory<T>()`, or rely on automatic tenant/filter-mode/user partitioning already applied on the `[Cacheable]` Persistence query path. `CachingOptions` has no global key prefix |
| Invalidate one subsystem only | `[InvalidatesCache(Category = typeof(...))]` |
| HTTP response caching | Output Cache Bridge via `CacheCategories.OutputCache` |
| External key construction | Use generated `{Type}CacheKeys.Create(...)` static helper |

---

## See Also

- [Getting Started](getting-started.md) -- Install, configure, and cache your first query
- [Category Routing](categories.md) -- Deep dive into subsystem isolation with categories
- [Common Mistakes](common-mistakes.md) -- Frequent errors with wrong/right/why format
- [Troubleshooting](troubleshooting.md) -- Problem/solution guide with diagnostics reference
