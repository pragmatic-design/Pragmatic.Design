---
title: "Getting Started with Pragmatic.Resilience"
description: "This guide covers adding resilience (retry, timeout, circuit breaker, bulkhead, hedging, rate limiter, fallback) to a Pragmatic.Design application."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Resilience/docs/getting-started.md
sidebar:
  order: 2
---
This guide covers adding resilience (retry, timeout, circuit breaker, bulkhead, hedging, rate limiter, fallback) to a Pragmatic.Design application.

## Prerequisites

- .NET 10.0+
- `Pragmatic.Result` (for error types)
- `Pragmatic.SourceGenerator` analyzer (for `[ResiliencePolicy]` integration with DomainActions)

## Step 1: Add the Package

```bash
dotnet add package Pragmatic.Resilience
```

## Step 2: Register Resilience Services

### Option A: Auto-Registration (SG)

When `Pragmatic.Resilience` is referenced, the Source Generator auto-registers `AddPragmaticResilience()` with default settings. No manual registration needed for basic usage.

### Option B: Explicit Registration

For custom policies, register in `IStartupStep`:

```csharp
[StartupStep]
public class AppStartupStep : IStartupStep
{
    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddPragmaticResilience(options =>
        {
            options.Policies["external-api"] = new ResiliencePolicyOptions
            {
                Timeout = new() { Timeout = TimeSpan.FromSeconds(10) },
                Retry = new() { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(200) },
                CircuitBreaker = new() { FailureThreshold = 5, BreakDuration = TimeSpan.FromSeconds(30) }
            };
        });
    }
}
```

You can also register individual named policies:

```csharp
services.AddPragmaticResilience();

services.AddResiliencePolicy("database", o =>
{
    o.Timeout = new() { Timeout = TimeSpan.FromSeconds(5) };
    o.Retry = new() { MaxRetries = 2, BackoffType = BackoffType.Constant, BaseDelay = TimeSpan.FromMilliseconds(100) };
});
```

## Step 3: Add Resilience to a DomainAction

Annotate a `DomainAction` with `[ResiliencePolicy]` to wrap its execution with a named pipeline:

```csharp
[DomainAction]
[ResiliencePolicy("external-api")]
public partial class FetchUserAction : DomainAction<UserDto>
{
    public override async Task<Result<UserDto, IError>> Execute(CancellationToken ct)
    {
        // This execution is wrapped by the "external-api" resilience pipeline.
        // Exceptions trigger retry/circuit breaker.
        // Result failures pass through unchanged (they are business errors).
        var response = await httpClient.GetAsync("/users/123", ct);
        // ...
    }
}
```

The source generator injects `IResiliencePipelineProvider` and wraps `ExecuteActionAsync` with the specified pipeline.

**Key design principle**: only exceptions trigger resilience strategies. `Result<T, E>` failures are business errors (validation, not found) and pass through the pipeline unchanged. Retrying a "not found" or "validation failed" would be wrong.

**When the pipeline itself gives up, you get a typed `Result` failure, not a thrown exception.** If every resilience strategy in the pipeline exhausts (retries exhausted, circuit open, timeout, bulkhead/rate-limit rejected, hedging exhausted), the generated invoker catches the resulting exception and maps it to the matching typed error (`RetryExhaustedError`, `CircuitBrokenError`, `TimeoutError`, `BulkheadRejectedError`, `RateLimitRejectedError`, `HedgingExhaustedError`) via `ResilienceResultBridge.TryMapToError`, returning it as a `Result` failure. This means an HTTP endpoint built on the action automatically gets the correct status code (503, 504, or 429) without catching anything. Non-resilience exceptions still propagate normally.

## Step 4: Use Pipelines Directly (Without DomainAction)

For code outside the action pattern, resolve `IResiliencePipelineProvider` and use a named pipeline:

```csharp
public class ExternalApiClient(IResiliencePipelineProvider pipelines)
{
    public async Task<string> FetchAsync(string url, CancellationToken ct)
    {
        var pipeline = pipelines.GetPipeline("external-api");

        return await pipeline.ExecuteAsync(
            (ctx, token) => httpClient.GetStringAsync(url, token),
            new ResilienceContext { OperationName = "FetchData" },
            ct);
    }
}
```

## Step 5: Configure via appsettings.json

Policies can be fully configured from JSON:

```json
{
  "Resilience": {
    "Default": {
      "Timeout": {
        "Timeout": "00:00:30",
        "TimeoutType": "Optimistic"
      }
    },
    "Policies": {
      "external-api": {
        "Timeout": { "Timeout": "00:00:10" },
        "Retry": {
          "MaxRetries": 3,
          "BaseDelay": "00:00:00.200",
          "BackoffType": "Exponential",
          "MaxDelay": "00:00:30",
          "UseJitter": true
        },
        "CircuitBreaker": {
          "FailureThreshold": 5,
          "BreakDuration": "00:00:30"
        }
      }
    }
  }
}
```

## Policy Resolution Order

When `GetPipeline(name)` is called:

1. **Fluent builder overrides** -- policies registered via `provider.AddPolicy(name, configure)` on `IResiliencePipelineRegistry`.
2. **Policy-options overrides** -- policies registered via `provider.AddPolicy(name, ResiliencePolicyOptions)`.
3. **Configuration** -- policies from `ResilienceOptions.Policies`, whether bound from `appsettings.json` or added via `AddResiliencePolicy(name, configure)`.
4. **Default** -- `ResilienceOptions.Default` if set.
5. **Passthrough** -- `PassthroughPipeline.Instance` (zero overhead, no wrapping).

`AddPolicy` and `MapOperation` live on `IResiliencePipelineRegistry` (which extends `IResiliencePipelineProvider`). If your service only injects `IResiliencePipelineProvider`, it can resolve pipelines but not register overrides -- inject `IResiliencePipelineRegistry` for that. `AddResiliencePolicy(name, configure)`, by contrast, writes into `ResilienceOptions.Policies` -- it is the configuration tier (3), not a fluent override.

Unknown policy names resolve to `PassthroughPipeline` -- no runtime errors, no overhead.

## Fluent Builder (No DI)

For standalone usage without DI:

```csharp
var stateStore = new InMemoryCircuitBreakerStateStore();

var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(o =>
    {
        o.MaxRetries = 3;
        o.BackoffType = BackoffType.Exponential;
    })
    .AddTimeout(o => o.Timeout = TimeSpan.FromSeconds(5))
    .AddCircuitBreaker(stateStore, o =>
    {
        o.FailureThreshold = 5;
        o.BreakDuration = TimeSpan.FromSeconds(30);
    })
    .Build();

var result = await pipeline.ExecuteAsync(
    (ctx, ct) => httpClient.GetStringAsync(url, ct),
    new ResilienceContext { OperationName = "FetchData" });
```

`ResiliencePipelineBuilder` also exposes `AddHedging(...)`, `AddRateLimiter(...)`, `AddFallback<TResult>(Func<CancellationToken, Task<TResult>>)` for typed results, and `AddFallback(Func<Exception, CancellationToken, Task>)` for void operations (compensating work when a void operation fails -- a typed `AddFallback<TResult>` never intercepts a void operation).

## DI Registrations

`AddPragmaticResilience()` registers:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `IResiliencePipelineProvider` | Singleton | Resolves named pipelines |
| `ICircuitBreakerStateStore` | Singleton | Default: `InMemoryCircuitBreakerStateStore` |
| `ResilienceOptions` | Singleton | Configuration (via `IOptions<ResilienceOptions>`) |

The registered singleton also implements `IResiliencePipelineRegistry` (resolvable as `IResiliencePipelineRegistry` via DI) -- inject that interface instead of `IResiliencePipelineProvider` when you need `AddPolicy()` or `MapOperation()`.

## Error Types

Resilience errors implement `Pragmatic.Result.Error` for integration with the Result pattern:

| Error | Code | HTTP Status | When |
|-------|------|-------------|------|
| `TimeoutError` | `TIMEOUT` | 504 | Operation exceeded timeout |
| `RetryExhaustedError` | `RETRY_EXHAUSTED` | 503 | All retry attempts failed |
| `CircuitBrokenError` | `CIRCUIT_BROKEN` | 503 | Circuit is open |
| `BulkheadRejectedError` | `BULKHEAD_REJECTED` | 429 | Max concurrency exceeded |
| `HedgingExhaustedError` | `HEDGING_EXHAUSTED` | 503 | All hedging attempts failed |
| `RateLimitRejectedError` | `RATE_LIMIT_REJECTED` | 429 | Request rate exceeded the configured window |

## Next Steps

- [Policies](/modules/resilience/policies/) -- detailed reference for each strategy
