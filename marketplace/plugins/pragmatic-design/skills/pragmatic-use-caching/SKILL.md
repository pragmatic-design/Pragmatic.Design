---
name: pragmatic-use-caching
description: Use when adding caching or invalidation to a query or mutation, or the user mentions cache keys, stampede, tags or a distributed cache — Pragmatic.Caching, [Cacheable], [InvalidatesCache], [CacheKey].
---

# Pragmatic Use Caching

**Covers:** Declarative caching with Pragmatic.Caching from NuGet — [Cacheable], [InvalidatesCache], [CacheKey], generated cache keys, tag-based invalidation, stampede protection, distributed backend.

`Pragmatic.Caching` makes caching **declarative**: decorate a query with `[Cacheable]` and whatever writes — a mutation, a `[DomainAction]`, an event — with `[InvalidatesCache]`; the source generator produces the cache key and the invalidation hook. You write neither keys nor invalidation logic by hand.

## When to use

- A query is expensive and the data changes infrequently.
- You want to invalidate the cache when a mutation modifies that data.

Do not use it on data that changes on every request or where strong consistency is mandatory.

## Package

```xml
<PackageReference Include="Pragmatic.Caching" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.1">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Namespace: `Pragmatic.Caching`, attributes in `Pragmatic.Caching.Attributes`. `ICacheStack` and option types are in `Pragmatic.Abstractions`.

## Mental model

```
   You decorate                Source generator produces      Runtime
  [Cacheable] on query     →  {Query}.Cache.g.cs           →  GetOrSetAsync with the generated key
  [InvalidatesCache] on     →  {Type}.CacheInvalidator.g.cs →  InvalidateByTagAsync on tags
   mutation/event
```

## Core patterns

### 1. Making a query cacheable

```csharp
using Pragmatic.Caching.Attributes;

[Cacheable(Duration = "10m", Tags = ["amenities"])]
public partial class SearchAmenitiesQuery
{
    public string? Category { get; init; }
    public int Page { get; init; } = 1;
}
```

- The class must be `partial` (**PRAG1700**). `Duration` accepts `Ns`/`Nm`/`Nh`/`Nd` (default `"5m"`); invalid format → **PRAG1701**.
- All public properties contribute to the cache key. The SG generates `GetCacheKey()` and `GetCacheOptions()` implementing `ICacheable`. The key has the form `{Namespace}.{TypeName}:Prop=value:...` — the type name is **fully qualified**, which is what stops two same-named queries in different namespaces from sharing an entry, and every value goes through `Uri.EscapeDataString`. For `MyApp.Catalog.SearchAmenitiesQuery`: `MyApp.Catalog.SearchAmenitiesQuery:Category=spa:Page=1`.
- `Sliding = true` approximates sliding expiration on the default `HybridCacheStack` backend as a shorter **absolute** L1 TTL — it is **not** refreshed on access (HybridCache has no true sliding expiration). `Priority` (`Low|Normal|High|NeverRemove`) is advisory metadata only — the default backend never reads it; it matters only to a custom `ICacheStack`.
- `Tags` support placeholders: `Tags = ["tenant:{TenantId}"]` expands to the property value at runtime (the property must exist, otherwise **PRAG1703**).

### 2. Controlling the cache key

```csharp
[CacheKey(Exclude = true)]  public DateTimeOffset RequestedAt { get; init; }   // excluded from the key
[CacheKey(Name = "cat", Order = 0)] public string? Category { get; init; }     // custom name and order
```

Without `[CacheKey]` every public property contributes to the key in declaration order.

⚠️ **Whose answer is it?** An entry computed for one caller and served to another is a data leak that
no test of the happy path catches. What the framework adds to the key for you:

| Cached | Partitioned by |
|---|---|
| a `[Query]` | the tenant always; the filter mode and disabled filters; **the user (and delegation) only when the entity has permission-based filters** (ownership, data scopes) |
| a `[Cacheable]` domain action | tenant, user and delegation, whenever they are present |

Anything else the result depends on must be a **property** of the cached type, so it is in the key —
and when that value is the caller, the invoker writes it, not the request:

```csharp
[Query<Order, OrderDto>]
[Cacheable(Duration = "5m", Tags = ["orders:user:{UserId}"])]
public partial class GetMyOrders
{
    [FromCurrentUser] public string UserId { get; private set; } = "";   // private setter: PRAG0730 otherwise
}
```

❌ A `GetMyOrders` that reads `ICurrentUser` inside and has no `UserId` property caches one list for
everybody. ❌ A `public Guid UserId { get; init; }` the caller fills puts the user in the key and lets
any caller ask for someone else's.

### 3. Invalidating on data change

```csharp
[InvalidatesCache("amenities")]                    // explicit tags
public partial class CreateAmenityMutation : Mutation<Amenity> { /* ... */ }
```

- Goes on a mutation or domain event, `partial` class (**PRAG1704**). The SG generates `InvalidateAsync` which calls `cache.InvalidateByTagAsync(...)`.
- `[InvalidatesCache]` without arguments uses the convention: the generator strips a trailing `Event`, `Updated`, `Created`, `Deleted`, or `Changed` suffix (whichever matches), then pluralizes and lowercases the rest — e.g. `ProductUpdated` → `products`, `OrderCreated` → `orders`.
- `Keys = [...]` to remove specific keys; `{Prop}` placeholders supported.
- Align the `Tags` of `[InvalidatesCache]` with those of `[Cacheable]`: the tag is what links the write to the read.

### 4. Direct cache stack usage

For imperative caching inject `ICacheStack`:

```csharp
var data = await _cache.GetOrSetAsync("report:monthly",
    async ct => await BuildReportAsync(ct), ct: ct);
```

`GetOrSetAsync` has **stampede protection** (only one factory executed per concurrent key). Other methods: `GetAsync`, `TryGetAsync`, `SetAsync`, `RemoveAsync`, `InvalidateByTagAsync`.

## Setup and backend

**HOST mode** (`PragmaticApp` / `Pragmatic.Composition` generates the host): caching is **auto-wired** — when `Pragmatic.Caching` is referenced, the SG's `RegisterAllPragmaticServices()` registers `AddHybridCache()` + `AddPragmaticCaching()` for you. Do not call them manually here.

**Manual/standalone setup** (no `Pragmatic.Composition` host — e.g. a plain console app or a hand-rolled `IServiceCollection`): nothing wires caching automatically. You must call both yourself, in order:

```csharp
builder.Services.AddHybridCache();       // Required first
builder.Services.AddPragmaticCaching();  // Registers ICacheStack -> HybridCacheStack
```

Skipping `AddHybridCache()` in manual mode fails at DI resolution with an `InvalidOperationException`: `HybridCacheStack` needs the `HybridCache` it wraps.

The default `HybridCacheStack` implementation is two-tier: L1 in-memory + L2 distributed. To activate the distributed tier add an `IDistributedCache` (e.g. Redis) **before** caching is registered — typically in an `IStartupStep`:

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    => services.AddStackExchangeRedisCache(o => o.Configuration = config["Redis"]);
```

### Categories

A category gives one subsystem its own key prefix, its own default duration and its own tag namespace,
so invalidating `user:42` for permissions does not evict the output cache entry with the same tag.

**Register it, then name it.** Naming a category that was never registered is not an error: it falls
back to the default stack, so the prefix and duration silently do not apply.

```csharp
// 1. register — in the host, or wherever AddPragmaticCaching is called
services.AddPragmaticCaching(cache =>
{
    cache.WithDefaultOptions(o => o.DefaultDuration = TimeSpan.FromMinutes(10));
    cache.ForCategory<CacheCategories.Permissions>(o => o.KeyPrefix = "perms:");
});

// 2. name it on the declaration
[Cacheable(Duration = "5m", Category = typeof(CacheCategories.Permissions))]
public partial class GetUserPermissionsQuery { public required string UserId { get; init; } }
```

`Category` works the same on `[InvalidatesCache]`, and the two must agree: an entry written through a
category is only reachable through that same category.

Predefined (`CacheCategories`): `Default`, `OutputCache`, `RateLimiting`, `Permissions`,
`Configuration`, `Idempotency`. Any type of your own works too.

To resolve a category's stack imperatively: `CacheStackProvider.ForCategory<TCategory>(serviceProvider)`.

### Invalidations across instances

A distributed cache shares the **entries**, not the **invalidations**. Every instance keeps its own
in-memory copy (L1), for the entry's whole duration. An invalidation run on one instance clears that
instance's copy and the Redis entry. Every other instance goes on serving its own copy.

With more than one instance, add the broadcast from the Redis package:

```csharp
services.AddPragmaticCaching();
services.AddStackExchangeRedisCache(o => o.Configuration = redis);   // shares the entries
services.AddRedisCacheInvalidationBroadcast(redis);                  // shares the invalidations
```

Every `RemoveAsync` and tag invalidation is then published on Redis pub/sub, and every other instance
drops its copy. What the broadcast does not do:

- **Delivery is at most once.** An instance that misses a message stays stale until its copy expires,
  which is what every instance was without the broadcast.
- **Only the decorated `ICacheStack` is broadcast.** Code that calls `HybridCache.RemoveAsync` directly
  is not.
- ⚠️ **A shorter L1 lifetime is not the fix.** It narrows the window and keeps the wrong answer.

### Counters across instances

`ICacheStack.IncrementAsync` on the default stack is atomic **within one process only**. Two instances
incrementing the same counter race on a read-modify-write and over-admit — which matters because rate
limits and quotas are what counters are for. For a multi-node deployment add the Redis package, which
moves the increment server-side into a single Lua `INCRBY`:

```csharp
services.AddPragmaticCaching();
services.AddRedisAtomicCounters("localhost:6379");   // or pre-register IConnectionMultiplexer
```

Everything else keeps using the existing backend; only `IncrementAsync` moves. Redis errors propagate
rather than falling back to the in-process counter, because a silent fallback would quietly weaken a
rate limit to per-instance.

## Common diagnostics

| ID | Sev | Trigger | Fix |
|---|---|---|---|
| **PRAG1700** | Error | `[Cacheable]` not `partial` | Add `partial` |
| **PRAG1701** | Warning | Invalid `Duration` format | Use `10m`, `2h`, ... |
| **PRAG1702** | Error | No properties for the key | Add a property or `[CacheKey]` |
| **PRAG1703** | Error | `{Prop}` placeholder without matching property | Fix the name |
| **PRAG1704** | Error | `[InvalidatesCache]` not `partial` | Add `partial` |
| **PRAG1750** | Warning | Two `[CacheKey]` properties share an `Order` | Give them distinct `Order` values — the key ordering is otherwise compiler-dependent |
| **PRAG1751** | Warning | Every property excluded from the key | Leave at least one, or the key is the type name alone and every call shares an entry |

## Troubleshooting

**Cache does not invalidate** — does the tag in `[InvalidatesCache]` match exactly the one in `[Cacheable]`? Tags with placeholders must expand to the same value.

**Stale data after a write** — does the operation that modifies the data have `[InvalidatesCache]` with the correct tag? An action counts as much as a mutation: the invalidation runs after its commit. ⚠️ If no `ICacheStack` is registered at all, the invalidation is skipped and logged at Warning rather than throwing — the write has already committed, so look for that line before assuming the tag is wrong.

**Generated members missing** — `Pragmatic.SourceGenerator` must be referenced as an analyzer.

## Build verification

```powershell
dotnet build                                              # PRAG17xx = caching issues
dotnet test path\to\App.Tests --no-restore -v minimal
```

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Showcase example application — code
that compiles and that `Showcase.IntegrationTests` and `Showcase.Host.Distributed.Tests` exercise — and
kept identical to it by the gate: a cacheable query and the mutation that drops its tag, a search whose
key is shortened with `[CacheKey]`, an invalidation from a domain action, and the Redis broadcast that
keeps two hosts in agreement.

Direct use of `ICacheStack` is used by no tested application yet, so there is no example of it here: the
sections above are the reference.
