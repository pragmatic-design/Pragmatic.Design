---
title: "Pragmatic.Caching"
description: "Source-generated caching for .NET 10 — typed keys, tag-based invalidation, category routing, and"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Caching/README.md
sidebar:
  order: 0
  label: Overview
---
Source-generated caching for .NET 10 — typed keys, tag-based invalidation, category routing, and
HybridCache integration.

## The Problem

Caching in .NET means scattered magic strings, manual serialization, hardcoded TTLs, and ad-hoc
invalidation:

```csharp
var key = $"product:{id}";                                  // magic string — rename and it breaks
var cached = await cache.GetStringAsync(key, ct);
if (cached is not null) return JsonSerializer.Deserialize<ProductDto>(cached);
var dto = ProductDto.FromEntity(await repo.GetByIdAsync(id, ct));
await cache.SetStringAsync(key, JsonSerializer.Serialize(dto),
    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, ct);
```

Miss one key in the invalidation handler and you serve stale data; share a backend across subsystems
and you get key collisions.

## The Solution

Declare **what** to cache and **when** to invalidate; the generator produces the key construction,
options, and invalidation at compile time.

```csharp
[Query<Product, ProductDto>(Single = true)]
[Cacheable(Duration = "5m", Tags = ["products", "product:{ProductId}"])]
public partial class GetProductQuery { public required Guid ProductId { get; init; } }

// A mutation invalidates by tag — no key-pattern duplication
[Mutation(Mode = MutationMode.Update)]
[InvalidatesCache("products")]
public partial class UpdateProductMutation : Mutation<Product> { /* ... */ }
```

Typed keys (no magic strings), tag-based invalidation, **category routing** to isolate subsystems
(permissions, rate limiting, output cache) on a shared backend, and HybridCache (in-memory +
distributed) integration.

## Distributed atomic counters (Redis)

`ICacheStack.IncrementAsync` is atomic **per process** with the default `HybridCacheStack` — enough for
single-instance rate limiting, not for a multi-node deployment. The `Pragmatic.Caching.Redis` package
routes counters to Redis (one atomic `INCRBY`+`PEXPIRE` Lua script), leaving every other cache
operation on the existing backend:

```csharp
services.AddPragmaticCaching();
services.AddRedisAtomicCounters("localhost:6379"); // or pre-register IConnectionMultiplexer
```

With this in place the endpoints' `[RateLimit]` limiter enforces limits strictly across instances.
Redis failures propagate (no silent per-instance fallback); the rate limiter fails closed.

## Installation

```bash
dotnet add package Pragmatic.Caching
dotnet add package Pragmatic.SourceGenerator   # generates keys + invalidation
```

## Status

`[Cacheable]`/`[InvalidatesCache]`, typed keys, tag invalidation, category routing, and HybridCache
integration are functional within 1.0.0-alpha. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/caching/concepts/) | Typed keys, tags, invalidation model, HybridCache |
| [Getting Started](/modules/caching/getting-started/) | Cache a query, invalidate on a mutation |
| [Categories](/modules/caching/categories/) | Category routing to isolate subsystems on a shared backend |
| [Common Mistakes](/modules/caching/common-mistakes/) | The most frequent caching pitfalls |
| [Troubleshooting](/modules/caching/troubleshooting/) | Problem/solution guide with diagnostics |

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/caching/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Caching is **MIT-licensed**.
