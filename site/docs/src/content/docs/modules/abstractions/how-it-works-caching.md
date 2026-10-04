---
title: "Caching: one contract, several backends, no dependency on any of them"
description: "> **Scope**: `src/Pragmatic.Abstractions/Caching/`, 5 files."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/caching.md
sidebar:
  order: 7
---
> **Scope**: `src/Pragmatic.Abstractions/Caching/`, 5 files.
> `ICacheStack` · `CacheEntryOptions` · `CacheFactoryResult<T>` · `CachePriority` · `CacheCategories`.
>
> **Not covered here**: the implementations live in `Pragmatic.Caching` and are named, not opened.
> The declarative surface (`[Cacheable]`, `[InvalidatesCache]`, `[CacheKey]`) is part of that
> module, not of Abstractions; see the `pragmatic-use-caching` skill. Consumers outside Abstractions
> are named where they matter.

For the member-by-member catalogue, see [interfaces](/modules/abstractions/interfaces/#9-caching). This document is
about how the pieces fit together, and why they are shaped that way.

## Why the contract lives here

All five files carry `// ReSharper disable once CheckNamespace`: the types are compiled into
`Pragmatic.Abstractions` but declare `namespace Pragmatic.Caching`.

That is the whole point. `Pragmatic.Authorization`, `Pragmatic.Configuration` and
`Pragmatic.Endpoints` all cache without taking a package reference on `Pragmatic.Caching`. Keeping
the namespace means the consumer writes `using Pragmatic.Caching;` whether or not the implementation
is present, and an application that never installs the caching module still compiles against the
contract. A module that needs more than the contract references the implementation and pays for it:
`Pragmatic.Endpoints.AspNetCore` and `Pragmatic.Persistence.EFCore` both do, the latter for
`ICacheStackResolver`, which lives in `Pragmatic.Caching`. Even there the stack itself stays
optional. `EfCoreQueryExecutor` takes it as `ICacheStack?`: no stack registered, no caching, no
failure.

## The ten members, and the four that carry a decision

Six of `ICacheStack`'s members are the expected shape: `GetOrSetAsync`, `GetAsync`, `TryGetAsync`,
`SetAsync`, `RemoveAsync`, `InvalidateByTagAsync`. `TryGetAsync` returns `(bool Found, T? Value)`
because a cached `null` and a cache miss are different events, and a nullable return cannot tell
them apart.

The other four are where the design shows.

**`GetOrSetAsync` with a `CacheFactoryResult<T>` factory.** The plain overload caches whatever the
factory returns, including the fallback value produced when a downstream call failed, which then
poisons the cache for the whole TTL. The second overload lets the factory decide: `Cache(value)` or
`DoNotCache(value)`. The caller gets the value either way. An implicit conversion from `T` means the
common "yes, cache it" path stays a bare `return value;`.

`DoNotCache` is an eviction, not a suppression. `HybridCacheEntryFlags` has to be fixed before the
factory runs, and `ShouldCache` is only known once it has run, so the value is written and then
removed: in that window a concurrent reader sees it, and a crash between the two leaves it until it
expires. What the overload guarantees is that the value does not survive its own TTL, not that it is
never visible.

**`RemoveByTagAsync`** is a default interface method that delegates to `InvalidateByTagAsync`. It
exists only so the `Remove*` family reads consistently; no implementer has reason to change its
behaviour, though a decorator such as `RedisCounterCacheStack` re-implements it to forward to its
inner stack; the default cannot do that on someone else's behalf.

**`InvalidateByTagsAsync`** is abstract, and its contract is stronger than it looks: every tag is
attempted, one failure does not stop the rest, and if any failed an `AggregateException` is thrown
*after* all of them have been tried. Bailing out on the first failure would leave later tags
un-invalidated while the caller believes the cache is clean, and stale data served confidently is
worse than an error. `InvalidateByTagAsync`, having one tag, simply propagates its failure.

**`IncrementAsync`** ships a default implementation, and the honest description of it is in the
contract itself:

| Deployment | Guarantee |
|---|---|
| Interface default (any backend) | none: a read-modify-write with no lock |
| `HybridCacheStack`, single instance | atomic per key: concurrent increments on one key cannot interleave |
| Multiple instances | none, unless the registered stack has a native atomic increment |

`HybridCacheStack` earns the middle row with a `ConcurrentDictionary<string, SemaphoreSlim>` of
per-key gates, pruned when uncontended, and re-confirming after acquisition that the gate is still
the one mapped for that key; otherwise the window between release and prune yields two critical
sections and a lost update. For strict cross-instance enforcement the answer is
`RedisCounterCacheStack` in `Pragmatic.Caching.Redis`, which overrides the method onto Redis `INCR`.
The `ttl` parameter is there for the rate-limiting case: pass the window and get fixed-window expiry.

## `CacheEntryOptions`

`Duration`, `SlidingDuration`, `Tags`, `Priority`, and three factories: `Default` (five minutes),
`WithDuration`, `WithSliding`.

Two things are worth knowing before you set a field:

- **Do not set `Duration` and `SlidingDuration` together.** Behaviour is provider-defined. Nothing in
  the type or in an analyzer stops you; the guidance is the guard.
- **`Priority` is advisory.** HybridCache exposes no priority concept, so the default stack cannot
  honour it. It is metadata a custom `ICacheStack` may choose to apply, which makes `CachePriority`
  (`Low`, `Normal`, `High`, `NeverRemove`) a vocabulary for your own implementation, not a lever on
  the shipped one.

`Tags` is an `ImmutableArray<string>` and is the input side of `InvalidateByTagsAsync`: tagging on
write is what makes group invalidation possible on the other end.

## Categories: routing, not configuration

A category is a sealed marker class used as a generic type argument. `CachingBuilder.ForCategory<T>()`
configures one; `CacheStackProvider.ForCategory<T>()` / `ForCategoryOrNull<T>()` resolves it. The
mechanism underneath is keyed DI with `typeof(TCategory).FullName` as the service key, and
`PrefixedCacheStack` as the decorator that applies the category's key prefix to the underlying stack.

This is why a category is a *type* and not a string: the key prefix and default TTL for
"permission sets" are chosen once, at registration, and every call site that names the category picks
them up without repeating them, and a typo is a compile error rather than a second, silently empty
cache namespace.

The framework routes four of the predefined markers:

| Category | Resolved by | For |
|---|---|---|
| `OutputCache` | `PragmaticOutputCacheStore` (`Pragmatic.Endpoints.AspNetCore`) | ASP.NET Core output cache bridge |
| `Idempotency` | `IdempotencyEndpointFilter` | response replay for `[Idempotent]` endpoints |
| `RateLimiting` | `DistributedRateLimiterExtensions`, building `PragmaticDistributedRateLimiter` | cross-instance counters |
| `Permissions` | `PermissionCacheStack` (`Pragmatic.Authorization`), consumed through `CachedPermissionResolver` | permission sets across requests |

Application categories are declared the same way (a static class of sealed markers) and need no
registration in Abstractions.

Not every cache goes through a category. `Pragmatic.Configuration` injects a plain `ICacheStack` into
`CachingConfigurationStore` and `CachingSecretStore`, and supplies its own
`InMemoryConfigurationCacheStack` as the default implementation; a component that resolves
`ICacheStack` directly gets the uncategorised stack.

## What the generator emits

There are no attributes in this folder: `[Cacheable]` and friends live in `Pragmatic.Caching`. What
the generator does with the types *here* is write them into generated code:

- **`CacheableTemplate`** emits a `private static readonly CacheEntryOptions` field when the options
  are constant, and a per-call construction when they are not, including the `Priority` assignment.
- **`InvalidatorTemplate`** emits, for a type marked `[InvalidatesCache]`, a partial of that type
  implementing `ICacheInvalidator`, with the stack as a parameter of `InvalidateAsync`: no
  constructor, no DI registration. The only automatic caller is the mutation invoker: on a domain
  event nothing invokes the generated method, and the invalidation has to be issued by hand.
- **`IdempotencyFilterHelper`** emits the generated half of the `[Idempotent]` filter. The
  `CacheCategories.Idempotency` stack is not named in that output: the filter resolves it at
  runtime.

Categories cross assembly boundaries through metadata rather than through a shared registry. A module
declares its categories as a `[PragmaticMetadata(Caching, …)]` assembly entry; the host generator's
`MetadataReader.ExtractCacheCategories` collects them from every referenced assembly, deduplicated and
ordered, and `PragmaticHostTemplate` registers one keyed stack per discovered category. A category
that a module never contributes as metadata therefore has no keyed stack in the host, and
`ForCategory<T>()` falls back to the unkeyed one: the category still caches, but without its prefix,
in the same key namespace as every other unregistered category. `ForCategoryOrNull<T>()` follows the
same chain and returns `null` only when no `ICacheStack` is registered at all: it answers "is
caching installed", not "is this category registered".

## External references

Named here, described where they live:

- **`Pragmatic.Caching` → `HybridCacheStack`**: the default implementation, L1 memory plus L2
  distributed via `Microsoft.Extensions.Caching.Hybrid`; registered by `AddPragmaticCaching()`.
- **`Pragmatic.Caching` → `PrefixedCacheStack`, `CacheStackProvider`, `CachingBuilder`**: the
  category machinery: prefix decorator, keyed resolution, and per-category configuration.
- **`Pragmatic.Caching.Redis` → `RedisCounterCacheStack`**: native atomic increment for
  cross-instance counters.
- **`Pragmatic.Persistence.EFCore` → `EfCoreQueryExecutor`**: takes `ICacheStack?`; caching is an
  enhancement, never a requirement.
- **`Pragmatic.Configuration` → `InMemoryConfigurationCacheStack`, `CachingConfigurationStore`,
  `CachingSecretStore`**: a module supplying its own stack rather than depending on the caching one.
