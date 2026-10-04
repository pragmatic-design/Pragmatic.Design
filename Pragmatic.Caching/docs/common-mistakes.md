# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Caching. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting `partial` on the Cacheable Type

**Wrong:**

```csharp
[Cacheable(Duration = "5m", Tags = ["products"])]
public class GetProductById
{
    public required int ProductId { get; init; }
}
```

**Compile result:** `PRAG1700` error -- "Type 'GetProductById' must be declared as partial to use [Cacheable]".

**Right:**

```csharp
[Cacheable(Duration = "5m", Tags = ["products"])]
public partial class GetProductById
{
    public required int ProductId { get; init; }
}
```

**Why:** The source generator emits `GetCacheKey()`, `GetCacheOptions()`, and the `ICacheable` interface implementation into a partial class. Without `partial`, the compiler cannot merge the generated code with your class. The same rule applies to `[InvalidatesCache]` types.

---

## 2. Using Invalid Duration Format

**Wrong:**

```csharp
[Cacheable(Duration = "5 minutes")]
public partial class GetUsers { }

[Cacheable(Duration = "300")]
public partial class GetOrders { }
```

**Compile result:** `PRAG1701` error -- "Invalid cache duration format '5 minutes'". The second example (`"300"`) also fails because bare numbers without a suffix are not a valid format.

**Right:**

```csharp
[Cacheable(Duration = "5m")]
public partial class GetUsers { }

[Cacheable(Duration = "300s")]    // 300 seconds
public partial class GetOrders { }

[Cacheable(Duration = "00:05:00")]  // TimeSpan format also works
public partial class GetProducts { }
```

**Why:** The source generator parses duration strings at compile time. Supported formats are:

| Format | Example | Meaning |
|--------|---------|---------|
| `Ns` | `"30s"` | N seconds |
| `Nm` | `"5m"` | N minutes |
| `Nh` | `"1h"` | N hours |
| `Nd` | `"1d"` | N days |
| TimeSpan string | `"00:15:00"` | 15 minutes |

Bare numbers, English words ("minutes"), and other formats are not recognized.

---

## 3. Tag Placeholders Referencing Non-Existent Properties

**Wrong:**

```csharp
[Cacheable(Duration = "5m", Tags = ["user:{UserId}"])]
public partial class GetUserProfile
{
    public required Guid Id { get; init; }  // Property is "Id", not "UserId"
}
```

**Compile result:** `PRAG1703` error -- "Placeholder '{UserId}' in tag/key refers to non-existent property on type 'GetUserProfile'".

**Right:**

```csharp
[Cacheable(Duration = "5m", Tags = ["user:{Id}"])]
public partial class GetUserProfile
{
    public required Guid Id { get; init; }
}
```

Or rename the property to match:

```csharp
[Cacheable(Duration = "5m", Tags = ["user:{UserId}"])]
public partial class GetUserProfile
{
    public required Guid UserId { get; init; }
}
```

**Why:** The source generator validates placeholder references at compile time. The placeholder name inside `{...}` must exactly match a public property name on the type. This is a compile-time safety check -- without it, placeholders would silently expand to empty strings at runtime, causing cache keys and tags that do not differentiate entries correctly.

---

## 4. Excluding All Properties from the Cache Key

**Wrong:**

```csharp
[Cacheable(Duration = "5m")]
public partial class GetGlobalConfig
{
    [CacheKey(Exclude = true)]
    public bool IncludeDebug { get; init; }

    [CacheKey(Exclude = true)]
    public string? Format { get; init; }
}
```

**Compile result:** `PRAG1751` warning -- "All properties on type 'GetGlobalConfig' are excluded from cache key generation. This may cause cache collisions." The code compiles, but the generated key is just `"GetGlobalConfig"` with no property discrimination -- every call returns the same cached result regardless of `IncludeDebug` or `Format`.

**Right:**

If the type truly has no differentiating parameters, remove the `[CacheKey(Exclude = true)]` attributes:

```csharp
[Cacheable(Duration = "5m")]
public partial class GetGlobalConfig
{
    // No properties = key is "GetGlobalConfig": intentional singleton cache
}
```

If `IncludeDebug` and `Format` should affect the cache key, keep them included:

```csharp
[Cacheable(Duration = "5m")]
public partial class GetGlobalConfig
{
    public bool IncludeDebug { get; init; }
    public string? Format { get; init; }
}
// Key: "GetGlobalConfig:IncludeDebug=True:Format=json"
```

**Why:** A cache key with no discriminating properties means every caller gets the same cached result. This is correct for a true singleton query (e.g., global configuration) but dangerous when different input combinations should produce different results. `PRAG1751` warns you to confirm the intent.

---

## 5. Forgetting to Register HybridCache Before AddPragmaticCaching

**Wrong:**

```csharp
// Missing: builder.Services.AddHybridCache()
builder.Services.AddPragmaticCaching();
```

**Runtime result:** `InvalidOperationException` when resolving `ICacheStack`, because `HybridCacheStack` requires `HybridCache` from DI. The constructor throws because the `HybridCache` dependency cannot be satisfied.

**Right:**

```csharp
builder.Services.AddHybridCache();           // Required: registers HybridCache
builder.Services.AddPragmaticCaching();      // Registers ICacheStack -> HybridCacheStack
```

For distributed caching (L2), also register a distributed cache backend before `AddHybridCache`:

```csharp
builder.Services.AddStackExchangeRedisCache(opts =>
{
    opts.Configuration = "localhost:6379";
});
builder.Services.AddHybridCache();
builder.Services.AddPragmaticCaching();
```

**Why:** `AddPragmaticCaching` registers `ICacheStack` as `HybridCacheStack`, which wraps `Microsoft.Extensions.Caching.Hybrid.HybridCache`. If `HybridCache` is not registered, the DI container cannot construct `HybridCacheStack`. `AddHybridCache()` is always required, even if you only use in-memory caching (L1).

**Exception -- HOST mode:** if your app bootstraps through `PragmaticApp` / `Pragmatic.Composition` (the generated host), you do **not** need to call either method manually. The SG detects the `Pragmatic.Caching` reference and the generated `RegisterAllPragmaticServices()` auto-registers both `AddHybridCache()` and `AddPragmaticCaching()` for you. This mistake applies specifically to **manual/standalone DI setups** (e.g. a console app or a host that does not use `PragmaticApp`), where nothing wires caching automatically and you must call both methods yourself, in order, as shown above.

---

## 6. Caching User-Specific Data Without Key Differentiation

**Wrong:**

```csharp
[Cacheable(Duration = "5m")]
public partial class GetMyOrders
{
    // UserId comes from ICurrentUser at runtime, not a property
}
```

The generated key is `"GetMyOrders"` -- the same for all users. User A's orders are cached, and User B sees them.

**Also wrong**: the value is a property, but the caller supplies it:

```csharp
[Cacheable(Duration = "5m")]
public partial class GetMyOrders
{
    public required Guid UserId { get; init; }  // "set from ICurrentUser before caching": by whom?
}
```

The key is per user now, and so is the hole: any caller that sends another user's id (in the query
string, or by building the query in process) reads that user's orders, and caches them under that
user's key.

**Right**: on a declared query, `[FromCurrentUser]`:

```csharp
[Query<Order, OrderDto>]
[Cacheable(Duration = "5m", Tags = ["orders:user:{UserId}"])]
public partial class GetMyOrders
{
    [FromCurrentUser]                              // ICurrentUser.Id; or a member of the [PragmaticUser] entity
    public string UserId { get; private set; } = "";
}
// Key: "GetMyOrders:UserId=abc-123", unique per user, and the user is the caller
```

The query's generated invoker fills `UserId` from the caller after validation and the permission
check, before the executor builds the key. It is still a public property, so it is in the key and in
the tag, and nobody else can write it: the setter is private (`PRAG0730` otherwise), and the property
is not a request parameter. The whole form:
[Filtering by the caller](../../Pragmatic.Persistence/docs/09-query-system.md#filtering-by-the-caller-fromcurrentuser).

**Why:** The source generator builds the cache key from public properties declared on the type. If the differentiating value (user ID, tenant ID) is not a property on the cacheable type, it is not included in the key. Always include any value that changes the result as a property on the cacheable type, and when that value is who is asking, let the invoker write it rather than the caller.

---

## 7. Broadcasting Invalidation When Targeted Would Suffice

**Wrong:**

```csharp
[InvalidatesCache("user:{UserId}")]
public partial class UserRolesChanged
{
    public Guid UserId { get; init; }
}
```

This broadcasts the invalidation to ALL registered cache categories. If you have OutputCache, Permissions, and RateLimiting categories registered, all three receive the invalidation call -- even though only the Permissions cache is affected by a role change.

**Right:**

```csharp
[InvalidatesCache("user:{UserId}", Category = typeof(CacheCategories.Permissions))]
public partial class UserRolesChanged
{
    public Guid UserId { get; init; }
}
```

**Why:** Broadcast invalidation (the default when `Category` is null) calls `InvalidateByTagAsync` on every registered category's `ICacheStack`. This is correct when the event affects multiple subsystems (e.g., a user deletion should clear both permission cache and output cache). But when only one subsystem is affected, targeted invalidation avoids unnecessary work and reduces the risk of evicting unrelated entries that happen to share a tag pattern.

---

## 8. Assuming `Sliding = true` Resets on Every Access

**Wrong assumption:**

```csharp
[Cacheable(Duration = "30m", Sliding = true)]
public partial class GetProductInventory
{
    public required int ProductId { get; init; }
}
```

It is tempting to read this as "the entry stays cached as long as it keeps getting read, and only expires after 30 minutes of inactivity." **That is not what happens on the default `HybridCacheStack` backend.** `Microsoft.Extensions.Caching.Hybrid.HybridCache` has no true sliding expiration. `Sliding = true` / `SlidingDuration` is mapped to the **L1 (local) cache's absolute expiration** -- a fixed, shorter per-tier TTL that is **not refreshed on read**. The entry expires from L1 exactly `SlidingDuration` after it was written, regardless of how much traffic hits it in between.

For write-heavy data like inventory counts, that means the entry silently expires (and the factory re-runs) on a fixed schedule -- it is not "kept alive forever by traffic" as the (incorrect) mental model of sliding expiration would suggest. The actual risk with write-heavy data is the more familiar one: stale data being served until the TTL lapses or an explicit invalidation happens.

**Right:** for write-heavy data, use a short absolute `Duration` combined with tag-based invalidation, and do not reach for `Sliding` at all -- it buys you nothing over a plain absolute duration on this backend:

```csharp
[Cacheable(Duration = "30s", Tags = ["inventory:{ProductId}"])]
public partial class GetProductInventory
{
    public required int ProductId { get; init; }
}

[InvalidatesCache("inventory:{ProductId}")]
public partial class InventoryChanged
{
    public int ProductId { get; init; }
}
```

**Why:** `Sliding` only ever shortens the L1 TTL; it never extends an entry's life based on access. Combined with a short absolute `Duration` and tag-based invalidation, stale data self-heals quickly even if an invalidation event is delayed or missed. If your design genuinely needs a window that resets on every read, `HybridCache` does not support it -- you would need a custom `ICacheStack` implementation.

---

## 9. Duplicate CacheKey Order Values

**Wrong:**

```csharp
[Cacheable(Duration = "5m")]
public partial class SearchProducts
{
    [CacheKey(Order = 0)]
    public required int TenantId { get; init; }

    [CacheKey(Order = 0)]    // Same Order as TenantId!
    public required string Category { get; init; }

    public int Page { get; init; } = 1;
}
```

**Compile result:** `PRAG1750` warning -- "Properties 'TenantId' and 'Category' have the same Order value 0. Order may be non-deterministic." The key might be `"MyApp.Catalog.SearchProducts:TenantId=1:Category=shoes:Page=1"` or `"MyApp.Catalog.SearchProducts:Category=shoes:TenantId=1:Page=1"` depending on compiler internals. Different builds could produce different key orderings, causing cache misses after deployment.

**Right:**

```csharp
[Cacheable(Duration = "5m")]
public partial class SearchProducts
{
    [CacheKey(Order = 0)]
    public required int TenantId { get; init; }

    [CacheKey(Order = 1)]
    public required string Category { get; init; }

    public int Page { get; init; } = 1;
}
// Key: "MyApp.Catalog.SearchProducts:TenantId=1:Category=shoes:Page=1", deterministic
```

**Why:** When two properties share the same `Order` value, their position in the cache key is non-deterministic. This can cause cache misses across builds or even across different compilation targets. Assign unique `Order` values to ensure deterministic key ordering. Properties without explicit `Order` use `int.MaxValue` and sort by declaration order.

---

## 10. Not Using Categories for Subsystem Isolation

**Wrong:**

```csharp
// Permission cache
[Cacheable(Duration = "5m", Tags = ["perms:{UserId}"])]
public partial class GetUserPermissions
{
    public required Guid UserId { get; init; }
}

// Output cache for product pages
[Cacheable(Duration = "1m", Tags = ["product:{ProductId}"])]
public partial class GetProductPage
{
    public required int ProductId { get; init; }
}
```

Both share the same default `ICacheStack` and namespace. There is no way to set different default TTLs for permissions vs. product pages without specifying `Duration` on every single attribute, and no per-subsystem key prefix (`CachingOptions` has no key prefix of its own).

**Right:**

```csharp
[Cacheable(Duration = "5m", Category = typeof(CacheCategories.Permissions), Tags = ["user:{UserId}"])]
public partial class GetUserPermissions
{
    public required Guid UserId { get; init; }
}

[Cacheable(Duration = "1m", Category = typeof(CacheCategories.OutputCache), Tags = ["product:{ProductId}"])]
public partial class GetProductPage
{
    public required int ProductId { get; init; }
}
```

With registration:

```csharp
builder.Services.AddPragmaticCaching(cache =>
{
    cache.ForCategory<CacheCategories.Permissions>(o =>
    {
        o.KeyPrefix = "perms:";
        o.DefaultDuration = TimeSpan.FromMinutes(5);
    });
    cache.ForCategory<CacheCategories.OutputCache>(o =>
    {
        o.KeyPrefix = "oc:";
        o.DefaultDuration = TimeSpan.FromMinutes(1);
    });
});
```

**Why:** Without categories, all cache entries share a single namespace. Category routing provides three benefits: (1) key prefix isolation prevents collisions, (2) per-category default durations reduce attribute verbosity, and (3) targeted invalidation avoids cross-subsystem eviction noise. Use categories whenever two subsystems have different caching requirements.

---

## 11. Manually Building Cache Keys Instead of Using Generated Helpers

**Wrong:**

```csharp
// In an invalidation handler:
var key = $"GetProductById:ProductId={evt.ProductId}";
await cache.RemoveAsync(key, ct);
```

This duplicates the key format from the `[Cacheable]` class. If the property name changes or the key format evolves, this string falls out of sync.

**Right:**

```csharp
// Use the generated static helper
var key = GetProductByIdCacheKeys.Create(evt.ProductId);
await cache.RemoveAsync(key, ct);
```

Or use tag-based invalidation instead of key-based removal:

```csharp
[InvalidatesCache("product:{ProductId}")]
public partial class ProductUpdated
{
    public int ProductId { get; init; }
}
```

**Why:** The source generator produces a `{Type}CacheKeys` static helper class with a `Create(...)` method that builds the cache key using the same format as `GetCacheKey()`. Using this helper ensures the key format stays in sync. Even better, prefer tag-based invalidation via `[InvalidatesCache]` -- it eliminates the need to know individual key formats entirely.

---

## Quick Reference

| Mistake | Diagnostic / Symptom |
|---------|---------------------|
| Missing `partial` | `PRAG1700` compile error |
| Invalid duration format | `PRAG1701` compile error |
| Placeholder references non-existent property | `PRAG1703` compile error |
| All properties excluded from key | `PRAG1751` warning, potential cache collisions |
| Missing `AddHybridCache()` | `InvalidOperationException` at runtime |
| No user-differentiating property | All users share the same cached result; use `[FromCurrentUser]` |
| Broadcast invalidation when targeted suffices | Unnecessary evictions in unrelated categories |
| Assuming `Sliding = true` resets on access | It does not on `HybridCacheStack` -- it is a shorter, non-refreshed L1 absolute TTL |
| Duplicate `[CacheKey(Order)]` values | `PRAG1750` warning, non-deterministic key ordering |
| No category isolation | Key collisions, configuration bleed, invalidation noise |
| Manual key construction | Key format drift when properties change |
