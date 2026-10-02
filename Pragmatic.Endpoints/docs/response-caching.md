# Response Caching

## The Problem

Many API endpoints return the same data for the same request parameters. A product catalog, a list of countries, a user's profile -- these change infrequently but are requested constantly. Without caching, every request hits your application logic, database, and serialization pipeline. This wastes server resources and increases latency for clients.

Response caching (also called output caching) stores the serialized HTTP response and serves it directly for subsequent identical requests, bypassing your endpoint logic entirely. Pragmatic.Endpoints provides the `[ResponseCache]` attribute to configure this per-endpoint, backed by ASP.NET Core's Output Cache middleware.

---

## `[ResponseCache]` Attribute

The attribute is defined in `Pragmatic.Endpoints.Attributes.ResponseCacheAttribute` and supports the following properties:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Duration` | `int` | `0` | Cache duration in seconds. Generates `CacheOutput(c => c.Expire(...))`. |
| `NoStore` | `bool` | `false` | When `true`, no output cache; the response carries `Cache-Control: no-store`. |
| `VaryByQueryKeys` | `string[]?` | `null` | Query string keys that create separate cache entries. Generates `c.SetVaryByQuery(...)`. |
| `VaryByHeaders` | `string[]?` | `null` | Request headers that create separate cache entries. Generates `c.SetVaryByHeader(...)`. |
| `Profile` | `string?` | `null` | Named cache tag for grouping. Generates `c.Tag("profile")`. |
| `Location` | `ResponseCacheLocation` | `Any` | Which form the SG generates — see below. |

### ResponseCacheLocation Enum

| Value | What is generated |
|-------|-------------------|
| `Any` | The server-side output cache: `CacheOutput(...)` with the duration and vary rules. Needs `app.UseOutputCache()`, and keeps **anonymous** answers only (see *Authenticated responses*) |
| `Client` | No server cache: the response carries `Cache-Control: private, max-age={Duration}`, so only the caller's own browser keeps it |
| `None` | `Cache-Control: no-store` |

---

## Basic Usage

### Cache for a Fixed Duration

```csharp
[Endpoint(HttpVerb.Get, "/api/products")]
[ResponseCache(Duration = 300)]  // 5 minutes
public partial class GetProducts : Endpoint<ProductListDto> { }
```

For 5 minutes after the first request, subsequent GET requests to `/api/products` receive the cached response without executing `HandleAsync`.

### Vary by Query Parameters

When different query parameters should produce different cache entries:

```csharp
[Endpoint(HttpVerb.Get, "/api/products")]
[ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "category", "page" })]
public partial class GetProducts : Endpoint<ProductListDto> { }
```

`/api/products?category=shoes&page=1` and `/api/products?category=hats&page=1` are cached independently.

### Private (Per-User) Caching

For user-specific responses that should not be cached by shared proxies:

```csharp
[Endpoint(HttpVerb.Get, "/api/user/preferences")]
[ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]
public partial class GetUserPreferences : Endpoint<PreferencesDto> { }
```

### Disable Caching

To explicitly mark an endpoint as non-cacheable (useful to override group-level defaults):

```csharp
[Endpoint(HttpVerb.Post, "/api/orders")]
[ResponseCache(NoStore = true)]
public partial class PlaceOrder : DomainAction<OrderId> { }
```

### Vary by Headers

When the response depends on request headers (e.g., `Accept-Language` for localized content):

```csharp
[Endpoint(HttpVerb.Get, "/api/content")]
[ResponseCache(Duration = 600, VaryByHeaders = new[] { "Accept-Language" })]
public partial class GetContent : Endpoint<ContentDto> { }
```

---

## How It Works: Source Generator to Output Cache

When the SG detects a `[ResponseCache]` attribute on an endpoint (and `NoStore` is not `true`), it emits a `.CacheOutput()` call in the endpoint's configuration:

```csharp
// Generated code
builder.CacheOutput(c => c.Expire(System.TimeSpan.FromSeconds(300)));
```

This uses ASP.NET Core's **Output Cache** (`Microsoft.AspNetCore.OutputCaching`), not the older Response Caching middleware. Output Cache is server-side: it stores the complete HTTP response in memory (or a distributed store) and serves it directly from the middleware pipeline.

The SG reads the `Duration` property and emits the corresponding `Expire()` call. The `VaryByQueryKeys` and `VaryByHeaders` properties flow as configuration to the output cache policy.

---

## In-Memory Default vs Distributed

### In-Memory (Default)

By default, ASP.NET Core's Output Cache stores responses in memory. This works well for single-instance deployments but has two limitations:

1. Each instance maintains its own cache, so in a multi-instance setup, cache hit rates are lower.
2. Memory pressure from large cached responses can impact your application.

### Distributed: UseOutputCacheFromPragmaticCaching

Pragmatic.Endpoints.AspNetCore provides a bridge that replaces the in-memory store with Pragmatic.Caching's `ICacheStack`. This means cached responses use the same backend (Redis, SQL Server, or whatever `IDistributedCache` you have configured) as your business-layer caching.

```csharp
using Pragmatic.Endpoints.AspNetCore.Extensions;

builder.Services.AddPragmaticCaching();                 // Register Pragmatic.Caching
builder.Services.UseOutputCacheFromPragmaticCaching();  // Bridge: output cache -> same backend
```

The `UseOutputCacheFromPragmaticCaching()` extension method does two things:

1. Calls `AddOutputCache()` to register the output cache services.
2. Registers `PragmaticOutputCacheStore` as the `IOutputCacheStore` singleton.

### PragmaticOutputCacheStore Internals

The store implements `IOutputCacheStore` with three operations:

| Operation | Behavior |
|-----------|----------|
| `GetAsync(key)` | Reads from `ICacheStack` with key `outputcache:{key}` |
| `SetAsync(key, value, tags, validFor)` | Writes to `ICacheStack` with the specified TTL. Tags are prefixed with `outputcache:tag:` |
| `EvictByTagAsync(tag)` | Invalidates all entries matching `outputcache:tag:{tag}` via `ICacheStack.InvalidateByTagAsync` |

The `outputcache:` prefix prevents collisions with business-layer cache entries. The store resolves `ICacheStack` lazily using `CacheStackProvider.ForCategory<CacheCategories.OutputCache>`, falling back to the default cache stack if no output-cache-specific category is configured.

If `ICacheStack` is not available at runtime (e.g., Pragmatic.Caching was not registered), all operations are no-ops -- the output cache degrades gracefully to no caching rather than throwing.

---

## `[ResponseCache]` vs `[Cacheable]`

These are different caching layers that solve different problems. They can coexist on the same endpoint.

| Aspect | `[ResponseCache]` | `[Cacheable]` |
|--------|-------------------|---------------|
| **Layer** | HTTP (response serialization) | Business logic (action result) |
| **What is cached** | Complete HTTP response (headers + body) | Action return value (before serialization) |
| **Where it runs** | ASP.NET Core Output Cache middleware | Pragmatic.Caching in the action invoker |
| **Cache key** | URL + query + headers | Action type + input parameters |
| **Invalidation** | Tag-based or TTL expiry | Explicit invalidation or TTL expiry |
| **Use case** | Reduce serialization and middleware overhead | Reduce database queries and computation |
| **Package** | Pragmatic.Endpoints | Pragmatic.Caching |

### When to Use Both

Consider an endpoint that returns a product catalog:

```csharp
[Endpoint(HttpVerb.Get, "/api/products")]
[ResponseCache(Duration = 60)]         // HTTP layer: serve cached response for 1 min
public partial class GetProducts : Endpoint<ProductListDto>
{
    // ...
}
```

If the underlying `GetProductsQuery` is also decorated with `[Cacheable]`, the caching happens at two levels:

1. **Output Cache** catches repeated HTTP requests and returns the cached response without entering the handler.
2. **Business Cache** catches the query result even if the output cache has expired but the data hasn't changed.

This layered approach is particularly effective when you have expensive queries behind frequently-accessed endpoints.

---

## Pipeline Requirement

Output caching requires its services and its middleware. **The generated host adds both** when a module
declares a shared `[ResponseCache]`: `services.AddOutputCache()` and `OutputCacheStep` (Order 95, after
authorization — a refused caller is refused before any cached answer is considered). A host that declares
none gets neither.

A host you build by hand adds them itself:

```csharp
app.UseOutputCache();         // after UseRouting, UseAuthentication, UseAuthorization
app.MapPragmaticEndpoints();
```

Without the middleware, the `CacheOutput()` configuration on endpoints has no effect.

---

## Complete Example

```csharp
// Program.cs or IStartupStep
services.AddPragmaticCaching();
services.UseOutputCacheFromPragmaticCaching();  // Optional: distributed backend

// Pipeline
app.UseOutputCache();
app.MapPragmaticEndpoints();
```

```csharp
// Cache product list for 5 minutes, vary by category and page
[Endpoint(HttpVerb.Get, "/api/products")]
[ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "category", "page" })]
public partial class GetProducts : Endpoint<ProductListDto>
{
    [FromQuery] public string? Category { get; set; }
    [FromQuery] public int? Page { get; set; }

    public override async Task<Result<ProductListDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        // This only executes if the output cache doesn't have a valid entry
        // ...
    }
}

// Private cache for user-specific data
[Endpoint(HttpVerb.Get, "/api/user/dashboard")]
[ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client)]
public partial class GetDashboard : Endpoint<DashboardDto> { }

// No caching on mutations
[Endpoint(HttpVerb.Post, "/api/products")]
[ResponseCache(NoStore = true)]
public partial class CreateProduct : DomainAction<ProductId> { }
```

---

## Notes

- **GET and HEAD only**: Output caching only applies to GET and HEAD requests by default. POST, PUT, DELETE responses are not cached.
- **Authenticated responses are never kept by the shared form.** ASP.NET's default output-cache policy
  does not cache a request whose user is authenticated, and varying by a header — `Authorization`
  included — does not change it. So one user's answer is not served to another, and `[ResponseCache]`
  with `Location = Any` on an authenticated route caches nothing, silently. Measured in
  `Pragmatic.Integration.Tests/WhatASharedResponseCacheKeepsTests`. For a signed-in user's data use
  `Location = ResponseCacheLocation.Client` (the browser keeps it); for server-side caching of a
  computed answer use `[Cacheable]`.
- **Group-level cache**: `EndpointGroupOptions.ResponseCacheDuration` sets a default cache duration for all endpoints in a group. Individual endpoints can override this with their own `[ResponseCache]` attribute.
- **NoStore**: When `NoStore = true`, the SG emits no `CacheOutput()` call and sets `Cache-Control: no-store` on the response. This is the correct way to explicitly mark an endpoint as uncacheable.
- **Output Cache vs Response Caching**: Pragmatic uses ASP.NET Core's Output Cache (`CacheOutput`), not the older Response Caching middleware (`ResponseCaching`). Output Cache is more capable: it supports tag-based invalidation, programmatic policies, and distributed stores.
