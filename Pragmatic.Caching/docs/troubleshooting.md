# Troubleshooting

Practical problem/solution guide for Pragmatic.Caching. Each section covers a common issue, the likely causes, and the fix.

---

## Cache Always Misses (Factory Called Every Time)

Your `GetOrSetAsync` factory runs on every request -- the cache appears to have no effect.

### Checklist

1. **Is `HybridCache` registered?** `AddHybridCache()` must be called before `AddPragmaticCaching()`. Without it, `HybridCacheStack` cannot be constructed and you will get a DI exception (or a test double that does not actually cache).

2. **Are you using the generated key?** The cache key must be identical between reads. If you build the key manually in one place and use `GetCacheKey()` in another, the format may differ:

   ```csharp
   // These produce different keys:
   var manualKey = $"product:{productId}";                    // "product:42"
   var generatedKey = query.GetCacheKey();                    // "GetProductById:ProductId=42"
   ```

3. **Is the duration too short?** A duration of `"1s"` means entries expire in 1 second. Verify the duration format is what you intend: `"5m"` is 5 minutes, not 5 milliseconds.

4. **Is the key varying unintentionally?** If a property changes between calls (e.g., a timestamp, a random correlation ID), the key changes and every call is a miss. Use `[CacheKey(Exclude = true)]` on properties that should not affect the cache key.

5. **For distributed cache (L2):** Is the distributed cache backend (Redis, SQL Server) reachable? `HybridCache` falls back to L1-only when L2 is unavailable, but in multi-instance deployments each instance has its own L1.

6. **Only in tests: is the clock pinned?** `HybridCache` takes its clock from the container's `TimeProvider` (the one `UseClock(testClock)` replaces) and treats an entry written *at or before* its tag's last invalidation as invalidated. With a frozen clock those two instants are equal, so after any invalidation of a tag (a write whose event carries `[InvalidatesCache]`, for instance) every entry written under that tag is born invalidated: the second read misses, in the whole suite and in a single test. Move the clock forward before a read that must hit (`clock.AdvanceSeconds(1)`), and only forward. Proven by `TagInvalidationUnderAPinnedClockTests`; the Temporal side is in Pragmatic.Temporal's testing guide, "Swapping the clock in DI".

---

## Stale Data After Mutation

A mutation changes data, but subsequent reads return the old cached value.

### Checklist

1. **Is there an `[InvalidatesCache]` on whatever wrote the rows?** The invalidation does not happen automatically -- you must mark the event, the mutation or the `[DomainAction]` with `[InvalidatesCache]` and specify the tags that match the cached query's tags.

2. **Do the tags match?** The invalidation tag must exactly match the tag on the cached entry. A cached entry with tag `"product:42"` is not evicted by invalidating `"products:42"` (note the plural "s").

3. **Are placeholders expanding correctly?** Verify that the property names in `{...}` placeholders match between the `[Cacheable]` tags and the `[InvalidatesCache]` tags:

   ```csharp
   // Cacheable tags
   Tags = ["product:{ProductId}"]

   // InvalidatesCache tags: must use the same property name
   [InvalidatesCache("product:{ProductId}")]   // Correct
   [InvalidatesCache("product:{Id}")]          // Wrong if property is "ProductId"
   ```

4. **Is InvalidateAsync being called?** If you implemented the invalidation manually instead of using the generated `ICacheInvalidator`, verify that the cache stack is the same instance. Category-specific invalidation must target the same category's `ICacheStack`.

5. **Is the invalidation targeted to the wrong category?** If the cached entry uses `Category = typeof(CacheCategories.Permissions)` but the invalidation has no category (broadcast), the prefixed keys may not match. Conversely, if the invalidation targets a specific category but the cached entry uses the default category, the invalidation misses entirely.

6. **Is there more than one instance?** Then the stale read may come from an instance that did not run the invalidation.
   - An invalidation clears the L1 of the instance that ran it, and the shared L2. Every other instance keeps its own L1 copy for the entry's whole duration.
   - With an event outbox, the instance that runs the invalidation is whichever one delivers the event.
   - Add `AddRedisCacheInvalidationBroadcast` from `Pragmatic.Caching.Redis` (see [More Than One Instance](concepts.md#more-than-one-instance)).
   - A shorter `Duration` only narrows the window.

---

## Category-Specific Cache Not Resolving

`CacheStackProvider.ForCategory<T>()` always returns the default `ICacheStack` instead of the category-specific one.

### Checklist

1. **Is the category registered?** Call `ForCategory<T>()` on the `CachingBuilder`:

   ```csharp
   builder.Services.AddPragmaticCaching(cache =>
   {
       cache.ForCategory<CacheCategories.Permissions>(o => o.KeyPrefix = "perms:");
   });
   ```

   Without this registration, no keyed service exists and `CacheStackProvider` falls back to the default.

2. **Is the category type correct?** The keyed service uses `typeof(TCategory).FullName` as the key. If you have two different `Permissions` classes in different namespaces, they resolve to different keys.

3. **Are you using Composition auto-registration?** With `PragmaticApp.RunAsync`, the SG auto-detects categories and registers them. However, the auto-registration uses an empty callback `(_ => { })` with auto-generated defaults. If you need custom configuration, override it explicitly in `Program.cs`.

---

## InvalidateAsync Not Evicting Entries

`InvalidateAsync` runs without errors, but cached entries remain.

### Checklist

1. **Check tag prefix isolation.** When categories are in use, `PrefixedCacheStack` prefixes both keys and tags. A cached entry stored through the Permissions category has its tags prefixed (e.g., `"perms:user:42"`). If the invalidation runs against the default (unprefixed) `ICacheStack`, it looks for tag `"user:42"` which does not match `"perms:user:42"`.

2. **Check that the invalidation targets the right cache stack.** If the cacheable type specifies `Category = typeof(CacheCategories.Permissions)`, the invalidation must also run against the same category's `ICacheStack`:

   ```csharp
   // Correct: targeted to the same category
   [InvalidatesCache("user:{UserId}", Category = typeof(CacheCategories.Permissions))]

   // Incorrect: broadcast may not match prefixed tags
   [InvalidatesCache("user:{UserId}")]
   ```

3. **Check HybridCache tag support.** `HybridCache.RemoveByTagAsync` requires tag tracking to be enabled. This is the default behavior, but verify that you have not configured `HybridCacheOptions` to disable tag tracking.

4. **Check for single tag failure tolerance.** `HybridCacheStack.InvalidateByTagsAsync` processes tags sequentially. If one tag invalidation fails, it catches the exception (logs at Warning level) and continues with the remaining tags. Check logs for `"Cache tag invalidation failed for tag='{tag}'"` warnings.

---

## HybridCacheStack Constructor Throws

`InvalidOperationException` or `NullReferenceException` when the application starts.

### Checklist

1. **Is `AddHybridCache()` called?** This is the most common cause. The correct registration order is:

   ```csharp
   builder.Services.AddHybridCache();        // 1. Required: HybridCache
   builder.Services.AddPragmaticCaching();   // 2. Registers ICacheStack -> HybridCacheStack
   ```

2. **Are you testing without DI?** In unit tests, create `HybridCacheStack` with a mock or in-memory `HybridCache`. The `logger` parameter is optional (defaults to `NullLogger`):

   ```csharp
   var hybridCache = /* mock or MemoryCache-based */;
   var cacheStack = new HybridCacheStack(hybridCache);
   ```

---

## Generated Code Not Compiling

The build fails with `PRAG17xx` diagnostics from the source generator.

### Diagnostics Reference

| ID | Severity | Cause | Fix |
|----|----------|-------|-----|
| `PRAG1700` | Error | Type is not `partial` | Add the `partial` keyword to the class/record/struct declaration |
| `PRAG1701` | Error | Invalid cache duration format | Use `"Ns"`, `"Nm"`, `"Nh"`, `"Nd"`, or TimeSpan string format `"HH:mm:ss"` |
| `PRAG1702` | Error | No cache key properties found | Add at least one public property, or remove `[CacheKey(Exclude = true)]` from all properties |
| `PRAG1703` | Error | Tag/key placeholder references non-existent property | Fix the placeholder name to match an existing public property: `{PropertyName}` |
| `PRAG1750` | Warning | Duplicate `[CacheKey(Order)]` values on two properties | Assign unique `Order` values to ensure deterministic key ordering |
| `PRAG1751` | Warning | All properties excluded from cache key | Keep at least one identifying property, or confirm the singleton key is intentional |

Check the **Error List** window in Visual Studio or the build output for diagnostic details and the affected source location.

### Is the SG analyzer referenced correctly?

In your `.csproj`, the Pragmatic.SourceGenerator must be referenced with `OutputItemType="Analyzer"`:

```xml
<ProjectReference Include="..\Pragmatic.SourceGenerator\Pragmatic.SourceGenerator.csproj"
                   OutputItemType="Analyzer"
                   ReferenceOutputAssembly="false" />
```

Without `OutputItemType="Analyzer"`, the SG is treated as a regular dependency and does not run.

---

## Cache Key Format Unexpected

The generated `GetCacheKey()` returns a key that does not match your expectations.

### Key Format

The generated key follows the pattern: `"{TypeName}:{Prop1Name}={Prop1Value}:{Prop2Name}={Prop2Value}"`.

- Properties are included in declaration order by default.
- `[CacheKey(Order = N)]` overrides the ordering (lower values first).
- `[CacheKey(Name = "alias")]` replaces the property name in the key segment.
- `[CacheKey(Exclude = true)]` removes the property from the key entirely.

Example:

```csharp
[Cacheable(Duration = "5m")]
public partial class GetUser
{
    [CacheKey(Order = 0)]
    public required int TenantId { get; init; }

    [CacheKey(Name = "id")]
    public required Guid UserId { get; init; }

    [CacheKey(Exclude = true)]
    public bool IncludeDeleted { get; init; }
}
// Key: "GetUser:TenantId=1:id=abc-123"
```

If you need to verify the exact key format, inspect the generated file at `obj/Debug/net10.0/generated/Pragmatic.SourceGenerator/{Type}.Cache.g.cs`.

---

## Metrics or Traces Not Appearing

`pragmatic.cache.hits` and other metrics are zero, or `Cache.GetOrSet` activities do not show in your tracing backend.

### Checklist

1. **Is the meter/activity source subscribed?** You must configure your OpenTelemetry exporter to listen to `"Pragmatic.Caching"`:

   ```csharp
   builder.Services.AddOpenTelemetry()
       .WithTracing(t => t.AddSource("Pragmatic.Caching"))
       .WithMetrics(m => m.AddMeter("Pragmatic.Caching"));
   ```

2. **Are activities sampled out?** If your tracing backend uses sampling, low-traffic environments may drop cache activities. Verify by setting `AlwaysOnSampler` temporarily.

3. **Is logging enabled at Debug level?** Hit/miss/set/remove messages are logged at `Debug` level. Ensure your logging configuration includes `Debug` for `Pragmatic.Caching`:

   ```json
   {
     "Logging": {
       "LogLevel": {
         "Pragmatic.Caching": "Debug"
       }
     }
   }
   ```

---

## FAQ

### Can I use ICacheStack without the source generator?

Yes. `ICacheStack` is a plain interface. You can inject it and call `GetOrSetAsync`, `SetAsync`, `RemoveAsync`, etc. directly with manual keys and options. The source generator automates key generation, options construction, and invalidation -- but it is not required for manual usage.

### How do I cache a value that might be null?

Use `TryGetAsync` instead of `GetAsync`. `GetAsync` returns `default(T)` on a cache miss, which is indistinguishable from a cached `null`. `TryGetAsync` returns a `(bool Found, T? Value)` tuple:

```csharp
var (found, value) = await cache.TryGetAsync<UserDto?>(key, ct);
if (!found)
{
    value = await LoadUserAsync(key, ct);
    await cache.SetAsync(key, value, options, ct);
}
```

### What happens when a tag invalidation fails for one tag out of many?

`HybridCacheStack.InvalidateByTagsAsync` processes tags sequentially. If one tag fails, the exception is caught and logged at Warning level (`"Cache tag invalidation failed for tag='{tag}'"`) and the remaining tags are still processed. The failure does not propagate to the caller.

### How do I test code that uses ICacheStack?

Create a simple in-memory implementation or use a mock. For integration tests, use the real `HybridCacheStack` with `AddHybridCache()` (in-memory only, no Redis needed). The `ICacheStack` interface has ten members -- all are straightforward to mock:

| Member | Purpose |
|--------|---------|
| `GetOrSetAsync<T>(key, Func<CancellationToken, ValueTask<T>>, options?, ct)` | Get-or-set with stampede protection |
| `GetOrSetAsync<T>(key, Func<CancellationToken, ValueTask<CacheFactoryResult<T>>>, options?, ct)` | Get-or-set where the factory decides whether to cache the result (conditional caching / "do not cache") |
| `GetAsync<T>` | Direct get, returns `default` on miss |
| `TryGetAsync<T>` | Get distinguishing miss from a cached `null`/`default` |
| `SetAsync<T>` | Explicit set |
| `RemoveAsync` | Remove by key |
| `InvalidateByTagAsync` | Remove all entries with a tag |
| `InvalidateByTagsAsync` | Remove all entries with any of several tags |
| `RemoveByTagAsync` | Default interface method, synonym for `InvalidateByTagAsync` |
| `IncrementAsync` | Atomic counter increment; process-local atomicity only unless a Redis-backed (or similar) `ICacheStack` overrides it |

### Can I have different backends per category (e.g., Redis for permissions, in-memory for rate limiting)?

Not in the current implementation. All categories route through the same `HybridCacheStack` backend, differentiated only by key prefix via `PrefixedCacheStack`. The `CachingBuilder.ForCategory<T>()` API is designed to support per-category backend isolation in a future version without breaking changes.

### What is the `{Type}CacheKeys` static helper for?

The source generator produces a static class `{Type}CacheKeys` with a `Create(...)` method. This lets you construct a cache key externally -- for example, to remove a specific entry by key when you know the parameters but do not have an instance of the cacheable type:

```csharp
var key = GetProductByIdCacheKeys.Create(productId: 42);
await cache.RemoveAsync(key, ct);
```

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working caching implementations.
- **Category Routing**: See [categories.md](categories.md) for detailed category configuration.
- **Getting Started**: See [getting-started.md](getting-started.md) for step-by-step setup.
- **Concepts**: See [concepts.md](concepts.md) for architecture and design rationale.
