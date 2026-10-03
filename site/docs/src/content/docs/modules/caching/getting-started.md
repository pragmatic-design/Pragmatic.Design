---
title: "Pragmatic.Caching -- Getting Started"
description: "This guide walks through adding caching to a query or DomainAction, customizing cache keys, setting up tag-based invalidation, and configuring category routing."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Caching/docs/getting-started.md
sidebar:
  order: 2
---
This guide walks through adding caching to a query or DomainAction, customizing cache keys, setting up tag-based invalidation, and configuring category routing.

## Prerequisites

- .NET 10.0+
- `Pragmatic.Caching` package
- `Pragmatic.SourceGenerator` analyzer reference
- `Microsoft.Extensions.Caching.Hybrid` registered in DI

## Step 1: Register Services

```csharp
// Program.cs or Startup
builder.Services.AddHybridCache();           // Required: Microsoft HybridCache
builder.Services.AddPragmaticCaching();      // Registers ICacheStack -> HybridCacheStack
```

For distributed caching (L2), also register a distributed cache backend:

```csharp
// Redis example
builder.Services.AddStackExchangeRedisCache(opts =>
{
    opts.Configuration = "localhost:6379";
});
builder.Services.AddHybridCache();
builder.Services.AddPragmaticCaching();
```

## Step 2: Make a Query Cacheable

Add `[Cacheable]` to your query class. The class must be `partial`:

```csharp
using Pragmatic.Caching.Attributes;

[Cacheable(Duration = "5m", Tags = ["products"])]
public partial class GetProductById
{
    public required Guid ProductId { get; init; }
}
```

The source generator produces a partial class implementing `ICacheable`:
- `GetCacheKey()` returns `"GetProductById:ProductId={value}"`
- `GetCacheOptions()` returns `CacheEntryOptions` with 5-minute duration and `["products"]` tags

## Step 3: Use the Cache in Your Code

Inject `ICacheStack` and use the generated methods:

```csharp
public class ProductQueryHandler(ICacheStack cache, IProductRepository repo)
{
    public async Task<ProductDto?> HandleAsync(GetProductById query, CancellationToken ct)
    {
        var key = query.GetCacheKey();
        var options = query.GetCacheOptions();

        return await cache.GetOrSetAsync(key, async token =>
            await repo.GetByIdAsync(query.ProductId, token), options, ct);
    }
}
```

`GetOrSetAsync` includes stampede protection: if 10 concurrent requests arrive for the same uncached key, only one factory call executes. The other 9 wait for its result.

## Step 4: Invalidate on Events

Mark domain events with `[InvalidatesCache]` to auto-generate invalidation logic:

```csharp
[InvalidatesCache("products", "tenant:{TenantId}")]
public partial class ProductCreated
{
    public Guid ProductId { get; init; }
    public int TenantId { get; init; }
}
```

The generator produces an `ICacheInvalidator` implementation:

```csharp
// Generated: calls cache.InvalidateByTagsAsync(["products", "tenant:42"])
await invalidator.InvalidateAsync(cacheStack, ct);
```

Convention-based invalidation (no explicit tags) derives the tag from the event name. The generator strips a trailing `Event`, `Updated`, `Created`, `Deleted`, or `Changed` suffix (whichever matches) before pluralizing and lowercasing the remainder:

```csharp
[InvalidatesCache]
public class ProductUpdated { }
// Strips "Updated" -> "Product" -> pluralize+lowercase -> invalidates the "products" tag

[InvalidatesCache]
public class OrderCreated { }
// Strips "Created" -> "Order" -> invalidates the "orders" tag
```

---

## Adding Categories

When you need cache isolation between subsystems (e.g., different TTLs, key prefixes, or future separate backends), use category routing.

### Step 5: Register with Categories

Switch from the simple `AddPragmaticCaching()` to the builder overload:

```csharp
builder.Services.AddHybridCache();
builder.Services.AddPragmaticCaching(cache =>
{
    cache.WithDefaultOptions(o => o.DefaultDuration = TimeSpan.FromMinutes(10));
    cache.ForCategory<CacheCategories.Permissions>(o =>
    {
        o.KeyPrefix = "perms:";
        o.DefaultDuration = TimeSpan.FromMinutes(5);
    });
});
```

Each `ForCategory<T>()` call registers a keyed `ICacheStack` backed by `PrefixedCacheStack`, which wraps the default `ICacheStack` with a key prefix and optional default duration.

If `KeyPrefix` is not explicitly set, it auto-generates from the category type name (e.g., `Permissions` becomes `permissions:`).

### Step 6: Use Category on Cacheable Types

```csharp
[Cacheable(Duration = "5m", Category = typeof(CacheCategories.Permissions))]
public partial class GetUserPermissions
{
    [CacheKey] public required Guid UserId { get; init; }
}
```

The generated `CacheCategory` property returns `typeof(CacheCategories.Permissions)`. At runtime, `CacheStackProvider.ForCategory<CacheCategories.Permissions>(sp)` resolves the category-specific `ICacheStack`.

### Step 7: Targeted vs Broadcast Invalidation

By default, `[InvalidatesCache]` broadcasts to all categories. Set `Category` to target a specific one:

```csharp
// Broadcast: invalidates across ALL categories
[InvalidatesCache("users:{UserId}")]
public class UserUpdated : IDomainEvent { public Guid UserId { get; init; } }

// Targeted: only invalidates the Permissions category
[InvalidatesCache("user:{UserId}", Category = typeof(CacheCategories.Permissions))]
public class UserRolesChanged : IDomainEvent { public Guid UserId { get; init; } }
```

---

## Customizing Cache Keys

### Controlling Key Order

Use `[CacheKey(Order)]` to control the order of segments in the key:

```csharp
[Cacheable(Duration = "10m")]
public partial class SearchProducts
{
    [CacheKey(Order = 0)]
    public required int TenantId { get; init; }

    [CacheKey(Order = 1)]
    public required string Category { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
// Key: "MyApp.Catalog.SearchProducts:TenantId={t}:Category={c}:Page={p}:PageSize={ps}"
// The type name is fully qualified, and every value goes through Uri.EscapeDataString.
```

### Excluding Properties

Some properties should not affect the cache key:

```csharp
[Cacheable(Duration = "5m")]
public partial class GetProducts
{
    public required string Category { get; init; }

    [CacheKey(Exclude = true)]
    public bool IncludeDebugInfo { get; init; }  // Not part of the key
}
```

### Custom Key Names

```csharp
[Cacheable(Duration = "5m")]
public partial class GetUser
{
    [CacheKey(Name = "id")]
    public required Guid UserId { get; init; }
}
// Key: "GetUser:id={value}" instead of "GetUser:UserId={value}"
```

---

## Tag-Based Invalidation

Tags allow invalidating groups of related cache entries without tracking individual keys.

### Declaring Tags

```csharp
[Cacheable(Duration = "10m", Tags = ["products", "tenant:{TenantId}"])]
public partial class GetProductsByTenant
{
    public required int TenantId { get; init; }
}
```

Tag placeholders (`{TenantId}`) are expanded at runtime using the property value:
- For `TenantId = 42`, the tags become `["products", "tenant:42"]`

---

## Manual Cache Operations

For scenarios beyond the attribute-based pattern:

```csharp
public class CartService(ICacheStack cache)
{
    // Set explicitly
    public async Task SetCartAsync(Guid userId, Cart cart, CancellationToken ct)
    {
        var key = $"cart:{userId}";
        var options = new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(30),
            Tags = [$"cart", $"user:{userId}"]
        };
        await cache.SetAsync(key, cart, options, ct);
    }

    // Get with miss detection
    public async Task<Cart?> GetCartAsync(Guid userId, CancellationToken ct)
    {
        var (found, cart) = await cache.TryGetAsync<Cart>($"cart:{userId}", ct);
        return found ? cart : null;
    }

    // Remove specific key
    public async Task ClearCartAsync(Guid userId, CancellationToken ct)
    {
        await cache.RemoveAsync($"cart:{userId}", ct);
    }

    // Invalidate all entries tagged with "cart"
    public async Task ClearAllCartsAsync(CancellationToken ct)
    {
        await cache.InvalidateByTagAsync("cart", ct);
    }
}
```

---

## Multi-Tenant Isolation

`CachingOptions` has no global key prefix. There are two mechanisms:

**1. Per-category key prefixes.** Register a category and set `KeyPrefix` on `CategoryCacheOptions` (applied by `PrefixedCacheStack`):

```csharp
builder.Services.AddPragmaticCaching(cache =>
{
    cache.ForCategory<TenantCacheCategory>(o => o.KeyPrefix = "tenant42:");
});
```

**2. Automatic tenant partitioning on the `[Cacheable]` query path.** If you cache a `Persistence` query result through the standard `[Cacheable]` + `ICacheStack` pipeline, you do not need to do anything: `EfCoreQueryExecutor` already partitions the cache key by tenant, filter mode, and the current user (when a permission-based filter guards the entity, or a navigation filter rewrites a collection the query reads) before the key ever reaches `ICacheStack`. Cross-tenant cache bleed does not occur on that path.

For manual/imperative cache usage outside the query pipeline, use tag-based isolation with `{TenantId}` placeholders in tags:

```csharp
[Cacheable(Duration = "5m", Tags = ["tenant:{TenantId}:products"])]
public partial class GetProducts
{
    public required int TenantId { get; init; }
}
```

---

## Eviction Priorities

> **Not honored by the default backend.** `Priority` is advisory metadata only. `HybridCacheEntryOptions` has no priority concept, so the default `HybridCacheStack` never reads or applies it. It has an effect only if you register a custom `ICacheStack` implementation that chooses to honor it.

```csharp
[Cacheable(Duration = "1h", Priority = CachePriority.High)]
public partial class GetSystemConfig { }

[Cacheable(Duration = "5m", Priority = CachePriority.Low)]
public partial class GetRecentSearches
{
    public required Guid UserId { get; init; }
}
```

Priority levels: `Low` (evict first), `Normal` (default), `High` (keep longer), `NeverRemove` (only expires by duration). These are meaningful only to a custom `ICacheStack` that implements priority-aware eviction.
