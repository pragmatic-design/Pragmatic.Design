---
title: "Rate Limiting"
description: "Public-facing APIs face a common threat: clients (malicious or accidental) sending too many requests in a short period. This can overwhelm your server, degrade "
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/docs/rate-limiting.md
sidebar:
  order: 12
---
## The Problem

Public-facing APIs face a common threat: clients (malicious or accidental) sending too many requests in a short period. This can overwhelm your server, degrade performance for legitimate users, and increase infrastructure costs. Rate limiting is the standard defense: you define how many requests a client can make within a time window, and requests exceeding that limit are rejected with HTTP 429 (Too Many Requests).

ASP.NET Core has a built-in rate limiting middleware, but configuring it manually for every endpoint is tedious and error-prone. Pragmatic.Endpoints integrates rate limiting at the attribute level, so you can protect an endpoint with a single line and the source generator handles the wiring.

---

## Three Modes

Pragmatic.Endpoints supports three approaches to rate limiting. Each is suited to different levels of control.

### Mode 1: Inline `[RateLimit]`

The simplest approach. Decorate an endpoint with `[RateLimit]` specifying the number of requests and a time window. The source generator creates a per-endpoint fixed-window limiter automatically.

```csharp
[Endpoint(HttpVerb.Post, "/api/auth/login")]
[RateLimit(Requests = 5, Window = "1m")]
public partial class Login : Endpoint<TokenResponse> { }
```

**When to use**: quick protection for individual endpoints. No additional configuration needed. Good for authentication, registration, or any sensitive operation where you know the exact limits up front.

### Mode 2: Named Policies via `ConfigureRateLimiter`

Define reusable policies in your startup configuration and reference them by name. This gives you access to all four ASP.NET Core rate limiting strategies and lets you share the same policy across multiple endpoints.

```csharp
// In startup / IStartupStep
services.AddPragmaticEndpoints(options =>
{
    options.ConfigureRateLimiter("standard", limiter =>
    {
        limiter.Strategy = RateLimiterStrategy.SlidingWindow;
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.SegmentsPerWindow = 6;
    });
});

// On the endpoint
[Endpoint(HttpVerb.Post, "/api/orders")]
[RateLimit(Policy = "standard")]
public partial class PlaceOrder : DomainAction<OrderId> { }
```

**When to use**: when multiple endpoints share the same limits, or when you need a strategy other than fixed window (sliding window, token bucket, concurrency).

### Mode 3: Raw ASP.NET Core Rate Limiting

You can bypass Pragmatic entirely and use ASP.NET Core's `AddRateLimiter` directly. This is useful if you need advanced partitioning, custom key generation, or integration with third-party rate limiting services.

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("custom-policy", limiter =>
    {
        limiter.PermitLimit = 50;
        limiter.Window = TimeSpan.FromMinutes(5);
    });
});
```

Then reference the policy name with `[RateLimit(Policy = "custom-policy")]`.

**When to use**: when you need full control over partitioning, custom key generators, or integration with external rate limiting infrastructure.

---

## How Inline `[RateLimit]` Works Under the Hood

When you write:

```csharp
[RateLimit(Requests = 5, Window = "1m")]
```

The source generator does two things:

### 1. Generates a Fixed-Window Limiter Policy

In `AddPragmaticEndpoints()`, the SG emits code that calls `AddRateLimiter` with a policy named `__pragmatic_ratelimit_{TypeName}`. For example, a `Login` endpoint gets the policy name `__pragmatic_ratelimit_Login`.

The generated code looks like:

```csharp
services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = rejectionCode;

    // Inline policies -- SG-generated from [RateLimit(Requests, Window)]
    rateLimiterOptions.AddFixedWindowLimiter("__pragmatic_ratelimit_Login", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = System.TimeSpan.FromMinutes(1);
    });
});
```

### 2. Attaches the Policy to the Endpoint

In the endpoint's `MapEndpoint` method, the SG emits:

```csharp
builder.RequireRateLimiting("__pragmatic_ratelimit_Login");
```

This connects the endpoint to its rate limiting policy.

### Window Format

The `Window` property accepts a human-readable duration string:

| Format | Meaning | Example |
|--------|---------|---------|
| `"30s"` | 30 seconds | `TimeSpan.FromSeconds(30)` |
| `"1m"` | 1 minute | `TimeSpan.FromMinutes(1)` |
| `"1h"` | 1 hour | `TimeSpan.FromHours(1)` |
| `"1d"` | 1 day | `TimeSpan.FromDays(1)` |

The SG parses these at compile time and emits the corresponding `TimeSpan.From*()` call.

### The `Policy` Property

When `Policy` is set, `Requests` and `Window` are ignored. The attribute simply references an existing named policy (either from `ConfigureRateLimiter` or from raw ASP.NET Core configuration):

```csharp
[RateLimit(Policy = "standard")]  // References a named policy
```

### The `KeyGenerator` Property

The `KeyGenerator` property accepts a `Type` that implements `IRateLimitKeyGenerator`. This allows custom partitioning logic (e.g., rate limit by tenant, by API key, or by a composite key).

> **Note:** `KeyGenerator` is parsed by the SG but not yet emitted in generated code. For custom partitioning, configure ASP.NET Core rate limiters directly via `AddRateLimiter()`.

---

## ConfigureRateLimiter: All Four Strategies

`PragmaticEndpointsOptions.ConfigureRateLimiter()` creates named policies in `AddPragmaticEndpoints()`. The SG emits `AddRateLimiter()` with all configured policies, supporting all four ASP.NET Core strategies:

### Fixed Window (Default)

A simple counter that resets at fixed intervals. If a client sends 100 requests in a 1-minute window, the 101st is rejected until the window resets.

```csharp
options.ConfigureRateLimiter("fixed", limiter =>
{
    limiter.Strategy = RateLimiterStrategy.FixedWindow;
    limiter.PermitLimit = 100;
    limiter.Window = TimeSpan.FromMinutes(1);
});
```

**Trade-off**: simple but susceptible to burst at window boundaries (a client can send 100 requests at :59 and another 100 at :00).

### Sliding Window

Divides the window into segments and tracks requests per segment. This smooths out the burst problem of fixed windows.

```csharp
options.ConfigureRateLimiter("smooth", limiter =>
{
    limiter.Strategy = RateLimiterStrategy.SlidingWindow;
    limiter.PermitLimit = 100;
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.SegmentsPerWindow = 6;  // 10-second segments
});
```

**SegmentsPerWindow** (default: 3) controls granularity. More segments = smoother rate limiting but slightly more memory.

### Token Bucket

Tokens are added to a bucket at a fixed rate. Each request consumes one token. When the bucket is empty, requests are rejected. This naturally allows controlled bursts (up to the bucket size) while enforcing a long-term rate.

```csharp
options.ConfigureRateLimiter("burst-friendly", limiter =>
{
    limiter.Strategy = RateLimiterStrategy.TokenBucket;
    limiter.PermitLimit = 100;        // Bucket capacity (max burst)
    limiter.Window = TimeSpan.FromMinutes(1);  // Replenishment period
    limiter.TokensPerPeriod = 10;     // Tokens added per period
    limiter.AutoReplenishment = true; // Default
});
```

If `TokensPerPeriod` is not set (0), it defaults to `PermitLimit`.

### Concurrency

Limits the number of concurrent requests rather than requests per time window. Useful for protecting resource-intensive endpoints (report generation, file processing).

```csharp
options.ConfigureRateLimiter("heavy-ops", limiter =>
{
    limiter.Strategy = RateLimiterStrategy.Concurrency;
    limiter.PermitLimit = 5;  // Max 5 concurrent requests
});
```

The `Window` property is ignored for concurrency limiters. `QueueLimit` controls how many excess requests are queued (default: 0, meaning immediate rejection).

### Queue Limit

All strategies support `QueueLimit`:

```csharp
limiter.QueueLimit = 10;  // Queue up to 10 excess requests instead of rejecting
```

When set to 0 (default), excess requests are rejected immediately with HTTP 429. When set to a positive number, excess requests wait in a queue and are processed when capacity becomes available.

---

## Rejection Status Code

By default, rate-limited requests receive HTTP 429 (Too Many Requests). You can change this globally:

```csharp
services.AddPragmaticEndpoints(options =>
{
    options.RateLimitRejectionStatusCode = 503; // Service Unavailable
});
```

The SG-generated `AddPragmaticEndpoints()` reads this value and passes it to `rateLimiterOptions.RejectionStatusCode`.

---

## Distributed Rate Limiting

In-memory rate limiters work per-instance. If you run multiple instances behind a load balancer, each instance has its own counters, so the effective limit is multiplied by the number of instances. For true cross-instance rate limiting, Pragmatic provides a bridge to Pragmatic.Caching.

### Setup

The bridge lives in `Pragmatic.Endpoints.AspNetCore`:

```csharp
using Pragmatic.Endpoints.AspNetCore.Extensions;

builder.Services.AddPragmaticCaching();  // Must be registered first
builder.Services.UseDistributedRateLimiterFromPragmaticCaching(
    "standard",
    permitLimit: 100,
    window: TimeSpan.FromMinutes(1));
```

### How It Works

`PragmaticDistributedRateLimiter` is a `System.Threading.RateLimiting.RateLimiter` implementation that stores counters in the distributed cache via `ICacheStack`. The implementation:

1. **Partitions by client identity** -- authenticated user name, or client IP address, or "anonymous" as fallback.
2. **Uses fixed-window semantics** -- window ID is derived from `DateTimeOffset.UtcNow.Ticks / window.Ticks`.
3. **Cache key format**: `ratelimit:{policyName}:{partitionKey}:{windowId}`.
4. **Window expiry via cache TTL** -- the counter entry expires when the window closes.
5. **Fail-open** -- if `ICacheStack` is not available (cache down), requests are permitted. This is a deliberate safety choice: cache failure should not cause a denial of service.

### Limitations

This is a best-effort distributed rate limiter, not a replacement for dedicated rate limiting services like Redis Rate Limit. There is an inherent race condition between the read and write of the counter (eventual consistency). For most applications this is acceptable, but for strict enforcement (billing, compliance), consider a dedicated solution.

---

## Pipeline Requirement

Rate limiting requires the ASP.NET Core rate limiting middleware in your pipeline:

```csharp
app.UseRateLimiter();  // Must be in the pipeline
app.MapPragmaticEndpoints();
```

Without `UseRateLimiter()`, the `RequireRateLimiting()` metadata on endpoints has no effect. The SG generates the policy registration and the metadata attachment, but the middleware is your responsibility to add.

---

## Complete Example

```csharp
// Program.cs or IStartupStep
services.AddPragmaticEndpoints(options =>
{
    options.RateLimitRejectionStatusCode = 429;

    options.ConfigureRateLimiter("api-standard", limiter =>
    {
        limiter.Strategy = RateLimiterStrategy.SlidingWindow;
        limiter.PermitLimit = 200;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.SegmentsPerWindow = 4;
    });

    options.ConfigureRateLimiter("heavy-operations", limiter =>
    {
        limiter.Strategy = RateLimiterStrategy.Concurrency;
        limiter.PermitLimit = 3;
    });
});

// Pipeline
app.UseRateLimiter();
app.MapPragmaticEndpoints();
```

```csharp
// Inline: 5 login attempts per minute
[Endpoint(HttpVerb.Post, "/api/auth/login")]
[RateLimit(Requests = 5, Window = "1m")]
public partial class Login : Endpoint<TokenResponse> { }

// Named policy: shared across all standard API endpoints
[Endpoint(HttpVerb.Get, "/api/products")]
[RateLimit(Policy = "api-standard")]
public partial class GetProducts : Endpoint<ProductListDto> { }

// Concurrency limit: max 3 simultaneous report generations
[Endpoint(HttpVerb.Post, "/api/reports/generate")]
[RateLimit(Policy = "heavy-operations")]
public partial class GenerateReport : DomainAction<ReportId> { }
```

---

## Notes

- **ASP.NET Core dependency**: Rate limiting requires `Microsoft.AspNetCore.RateLimiting`. The SG adds the `using` directive automatically when inline rate limits are detected.
- **Inline is always fixed window**: The `[RateLimit(Requests, Window)]` shorthand generates a `FixedWindowLimiter`. For other strategies, use `ConfigureRateLimiter` with a named policy.
- **Policy name collision**: Inline policies use the convention `__pragmatic_ratelimit_{TypeName}`. Avoid naming your custom policies with this prefix.
- **Idempotent registration**: If multiple endpoints use inline rate limits, they are all registered in a single `AddRateLimiter` call in the generated `AddPragmaticEndpoints()` method.
- **Group-level rate limiting**: `EndpointGroupOptions` has a `RateLimitPolicy` property for applying a rate limit policy to all endpoints in a group.
