---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.Resilience. Each section shows the wrong approach, the correct approach, and explains"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Resilience/docs/common-mistakes.md
sidebar:
  order: 4
---
These are the most common issues developers encounter when using Pragmatic.Resilience. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Retrying Business Errors (Result Failures)

**Wrong:**

```csharp
public class OrderService(IResiliencePipelineProvider pipelines)
{
    public async Task<Result<OrderDto, IError>> PlaceOrderAsync(OrderRequest request, CancellationToken ct)
    {
        var pipeline = pipelines.GetPipeline("orders");

        // Wrapping the entire flow including business logic
        return await pipeline.ExecuteAsync(async (ctx, token) =>
        {
            var validation = Validate(request);
            if (!validation.IsValid)
                throw new InvalidOperationException(validation.Error); // Throws to trigger retry!

            return await _orders.CreateAsync(request, token);
        }, new ResilienceContext { OperationName = "PlaceOrder" }, ct);
    }
}
```

**Runtime result:** A validation failure throws an exception that triggers the retry strategy. The same invalid request is retried 3 times with exponential backoff, wasting 1-2 seconds before ultimately failing. If the request is invalid, it will always be invalid.

**Right:**

```csharp
public class OrderService(IResiliencePipelineProvider pipelines)
{
    public async Task<Result<OrderDto, IError>> PlaceOrderAsync(OrderRequest request, CancellationToken ct)
    {
        ValidationError validation = Validate(request);
        if (validation.IsFailure)
            return validation; // Return Result failure, no retry

        var pipeline = pipelines.GetPipeline("orders");

        // Only wrap the external/infrastructure call
        return await pipeline.ExecuteAsync(
            (ctx, token) => _orders.CreateAsync(request, token),
            new ResilienceContext { OperationName = "PlaceOrder" }, ct);
    }
}
```

**Why:** Resilience strategies only trigger on exceptions, by design. Business validation, "not found" conditions, and authorization failures are expected outcomes that should be returned as `Result` failures. Only wrap the infrastructure call that can actually fail transiently (database, HTTP, message queue). When using `[ResiliencePolicy]` on a DomainAction, this separation is automatic -- `Result` failures pass through the pipeline unchanged.

---

## 2. Missing the Circuit Breaker State Store in the Fluent Builder

**Wrong:**

```csharp
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(o => o.MaxRetries = 3)
    .AddCircuitBreaker(???, o => o.FailureThreshold = 5)  // What state store?
    .Build();
```

**Compile result:** `AddCircuitBreaker` requires an `ICircuitBreakerStateStore` parameter. Without DI, you must provide one explicitly.

**Right:**

```csharp
var stateStore = new InMemoryCircuitBreakerStateStore();

var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(o => o.MaxRetries = 3)
    .AddCircuitBreaker(stateStore, o => o.FailureThreshold = 5)
    .Build();
```

**Why:** Circuit breaker state must persist across pipeline executions. The `InMemoryCircuitBreakerStateStore` is a thread-safe, per-process store. When using DI (`AddPragmaticResilience()`), the state store is registered automatically as a singleton and shared across all circuit breakers. When using the fluent builder directly, you must provide the instance yourself. For distributed scenarios, implement `ICircuitBreakerStateStore` with a Redis or database backend.

---

## 3. Setting Timeout Shorter Than Retry Total Duration

**Wrong:**

```csharp
services.AddResiliencePolicy("external-api", o =>
{
    o.Timeout = new() { Timeout = TimeSpan.FromSeconds(5) };
    o.Retry = new()
    {
        MaxRetries = 3,
        BaseDelay = TimeSpan.FromSeconds(2),
        BackoffType = BackoffType.Exponential
        // Delays: 2s, 4s, 8s = 14s of delay alone, plus execution time
    };
});
```

**Runtime result:** The timeout wraps the entire pipeline (Order 100 is more external than Retry at Order 400). After 5 seconds, the timeout fires and cancels everything -- including pending retries. With exponential backoff starting at 2 seconds, you may only get 1-2 retry attempts before the timeout kills the pipeline. The third retry never executes.

**Right:**

```csharp
services.AddResiliencePolicy("external-api", o =>
{
    o.Timeout = new() { Timeout = TimeSpan.FromSeconds(30) };  // Room for retries
    o.Retry = new()
    {
        MaxRetries = 3,
        BaseDelay = TimeSpan.FromMilliseconds(200),            // Short delays
        BackoffType = BackoffType.Exponential,
        MaxDelay = TimeSpan.FromSeconds(5)                     // Capped
    };
});
```

**Why:** Timeout is at Order 100 (outermost) and Retry is at Order 400 (inner). The timeout measures total wall-clock time including all retries and their delays. Set the timeout to at least `(MaxRetries + 1) * expectedOperationDuration + totalDelayTime`. A good rule of thumb: timeout should be 2-3x the expected total retry duration.

---

## 4. Not Filtering Which Exceptions Trigger Retry

**Wrong:**

```csharp
o.Retry = new RetryOptions
{
    MaxRetries = 3,
    // ShouldRetry is null -- all exceptions trigger retry
};
```

**Runtime result:** Every exception is retried, including `ArgumentException`, `InvalidOperationException`, `NullReferenceException`, and other programming errors. A bug in your code causes 3 unnecessary retries before the exception finally propagates.

**Right:**

```csharp
o.Retry = new RetryOptions
{
    MaxRetries = 3,
    ShouldRetry = ex => ex is HttpRequestException
                      or TimeoutRejectedException
                      or IOException
};
```

**Why:** The default `ShouldRetry = null` means "retry on all exceptions." This is convenient for prototyping but dangerous in production. Programming errors (`ArgumentException`, `NullReferenceException`, `InvalidCastException`) should not be retried -- they will fail every time. Explicitly filter to transient exceptions: `HttpRequestException`, `TimeoutRejectedException`, `IOException`, database connection exceptions, etc.

---

## 5. Creating a New InMemoryCircuitBreakerStateStore Per Pipeline

**Wrong:**

```csharp
public class MyService
{
    public async Task<string> CallExternalApiAsync(CancellationToken ct)
    {
        // New state store every time -- circuit state is lost between calls!
        var stateStore = new InMemoryCircuitBreakerStateStore();

        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(stateStore, o => o.FailureThreshold = 5)
            .Build();

        return await pipeline.ExecuteAsync(
            (ctx, token) => http.GetStringAsync(url, token),
            new ResilienceContext { OperationName = "FetchData" }, ct);
    }
}
```

**Runtime result:** Each call creates a new state store and a new pipeline. The circuit breaker never accumulates failures because its state is discarded after every call. The circuit will never open, making the circuit breaker useless.

**Right:**

```csharp
public class MyService
{
    // Shared state store -- persists across calls
    private static readonly InMemoryCircuitBreakerStateStore StateStore = new();
    private static readonly IResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddCircuitBreaker(StateStore, o => o.FailureThreshold = 5)
        .Build();

    public async Task<string> CallExternalApiAsync(CancellationToken ct)
    {
        return await Pipeline.ExecuteAsync(
            (ctx, token) => http.GetStringAsync(url, token),
            new ResilienceContext { OperationName = "FetchData" }, ct);
    }
}
```

Or, better, use DI:

```csharp
public class MyService(IResiliencePipelineProvider pipelines)
{
    public async Task<string> CallExternalApiAsync(CancellationToken ct)
    {
        var pipeline = pipelines.GetPipeline("external-api");  // Cached internally
        return await pipeline.ExecuteAsync(
            (ctx, token) => http.GetStringAsync(url, token),
            new ResilienceContext { OperationName = "FetchData" }, ct);
    }
}
```

**Why:** `IResiliencePipelineProvider` caches pipelines by name using `ConcurrentDictionary`. The `ICircuitBreakerStateStore` is registered as a singleton. This means all operations using the same policy name share the same circuit state. When using the fluent builder, you must ensure the state store instance and pipeline are shared (static field, DI singleton, etc.).

---

## 6. Using Pessimistic Timeout for CancellationToken-Aware Operations

**Wrong:**

```csharp
o.Timeout = new TimeoutOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    TimeoutType = TimeoutType.Pessimistic  // Uses Task.WhenAny
};

// Used with HttpClient, which already honors CancellationToken
await pipeline.ExecuteAsync(
    (ctx, ct) => httpClient.GetStringAsync(url, ct),
    context);
```

**Runtime result:** The pessimistic timeout races `Task.Delay` against the HTTP call via `Task.WhenAny`. When the timeout fires, it cancels the `CancellationToken`, but the HTTP call may have already started reading the response stream. The background task continues consuming resources (socket, memory) even after the timeout returns. With optimistic timeout, the linked `CancellationToken` would cancel the HTTP call cleanly.

**Right:**

```csharp
o.Timeout = new TimeoutOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    TimeoutType = TimeoutType.Optimistic  // Default -- uses linked CancellationToken
};
```

**Why:** Optimistic timeout creates a linked `CancellationToken` and cancels it after the duration. Any operation that accepts `CancellationToken` (HttpClient, EF Core, Dapper, most async .NET APIs) will cancel cooperatively. Pessimistic timeout is only needed for operations that do not honor cancellation: legacy synchronous code, third-party libraries that ignore tokens. Using pessimistic mode unnecessarily wastes resources because the abandoned task continues running in the background.

---

## 7. Forgetting to Set OperationName on ResilienceContext

**Wrong:**

```csharp
var pipeline = pipelines.GetPipeline("external-api");

// ResilienceContext requires OperationName
await pipeline.ExecuteAsync(
    (ctx, ct) => http.GetStringAsync(url, ct),
    new ResilienceContext { },  // Missing required OperationName!
    ct);
```

**Compile result:** `OperationName` is declared as `required string`, so this is a compile error: `Required member 'ResilienceContext.OperationName' must be set`.

**Right:**

```csharp
await pipeline.ExecuteAsync(
    (ctx, ct) => http.GetStringAsync(url, ct),
    new ResilienceContext { OperationName = "FetchUserProfile" },
    ct);
```

**Why:** `OperationName` is used in structured logging, metrics tags, activity names, and circuit breaker key resolution. Without it, logs would show `null` and metrics would be untagged, making observability useless. The `required` keyword on the property enforces this at compile time.

---

## 8. Configuring Circuit Breaker Without Understanding the Threshold

**Wrong:**

```csharp
o.CircuitBreaker = new CircuitBreakerOptions
{
    FailureThreshold = 1,  // Opens after a single failure!
    BreakDuration = TimeSpan.FromMinutes(5)
};
```

**Runtime result:** A single transient failure (network blip, DNS hiccup) opens the circuit for 5 minutes. All subsequent requests are rejected immediately for 5 minutes, even though the service may have recovered in milliseconds. One bad request takes out the entire integration for 5 minutes.

**Right:**

```csharp
o.CircuitBreaker = new CircuitBreakerOptions
{
    FailureThreshold = 5,   // Allow several failures before opening
    BreakDuration = TimeSpan.FromSeconds(30)  // Short break, probe quickly
};
```

**Why:** The circuit breaker uses consecutive failure counting. A threshold of 1 means any single exception opens the circuit. Combine this with a long break duration, and a harmless transient failure causes extended downtime. Start with `FailureThreshold = 5` and `BreakDuration = 30s` as a baseline, then tune based on observed failure patterns. The goal is to detect a genuinely unhealthy service (sustained failures), not a one-off blip.

---

## 9. Wrapping the Entire DomainAction Instead of Using [ResiliencePolicy]

**Wrong:**

```csharp
[DomainAction]
public partial class FetchUserAction : DomainAction<UserDto>
{
    private IResiliencePipelineProvider _pipelines = null!;

    public override async Task<Result<UserDto, IError>> Execute(CancellationToken ct)
    {
        var pipeline = _pipelines.GetPipeline("external-api");

        // Manually wrapping inside Execute -- loses Result-awareness
        try
        {
            return await pipeline.ExecuteAsync(async (ctx, token) =>
            {
                var user = await _userService.GetByIdAsync(Id, token);
                if (user is null)
                    throw new Exception("Not found"); // Forced to throw to return from lambda!
                return Result<UserDto, IError>.Success(UserDto.From(user));
            }, new ResilienceContext { OperationName = "FetchUser" }, ct);
        }
        catch (RetryExhaustedException ex)
        {
            return new RetryExhaustedError("FetchUser", 3, ex.InnerException);
        }
    }
}
```

**Runtime result:** Manual wrapping inside `Execute` forces awkward patterns: you must throw exceptions to exit the lambda, then catch resilience exceptions to convert them to `Result`. The code is verbose and error-prone.

**Right:**

```csharp
[DomainAction]
[ResiliencePolicy("external-api")]
public partial class FetchUserAction : DomainAction<UserDto>
{
    public override async Task<Result<UserDto, IError>> Execute(CancellationToken ct)
    {
        var user = await _userService.GetByIdAsync(Id, ct);
        if (user is null)
            return NotFoundError.For("User", Id);  // Result failure -- not retried

        return UserDto.From(user);
    }
}
```

**Why:** The `[ResiliencePolicy("name")]` attribute tells the source generator to inject `IResiliencePipelineProvider` and wrap `Execute` at the invoker level. The wrapping happens outside your code -- exceptions trigger resilience strategies, and `Result` failures pass through unchanged. No manual try/catch, no lambda gymnastics, no explicit pipeline resolution.

This also means the "Wrong" example's manual `catch (RetryExhaustedException ex)` is unnecessary work, not just verbose: with `[ResiliencePolicy]`, when the pipeline itself exhausts (retry exhausted, circuit open, timeout, bulkhead/rate-limit rejected, hedging exhausted), the generated invoker maps the exception to the matching typed error (`RetryExhaustedError`, `CircuitBrokenError`, etc.) via `ResilienceResultBridge.TryMapToError` and returns it as a `Result` failure automatically -- no resilience exception ever escapes to the caller for you to catch.

---

## 10. Adding Bulkhead Without Considering Queue Timeout

**Wrong:**

```csharp
o.Bulkhead = new BulkheadOptions
{
    MaxConcurrency = 5,
    MaxQueuedActions = 100,
    // QueueTimeout defaults to TimeSpan.Zero
};
```

**Runtime result:** `MaxQueuedActions = 100` suggests you want to queue overflow requests. But `QueueTimeout = TimeSpan.Zero` means the queue wait timeout is zero -- `SemaphoreSlim.WaitAsync(0)` returns immediately if no slot is available. The queue is effectively disabled despite being configured. All overflow requests are rejected instantly with `BulkheadRejectedException`.

**Right:**

```csharp
o.Bulkhead = new BulkheadOptions
{
    MaxConcurrency = 5,
    MaxQueuedActions = 100,
    QueueTimeout = TimeSpan.FromSeconds(5)  // Wait up to 5s for a slot
};
```

Or, if you intentionally want no queuing (reject immediately):

```csharp
o.Bulkhead = new BulkheadOptions
{
    MaxConcurrency = 5,
    MaxQueuedActions = 0,        // Explicitly: no queue
    // QueueTimeout irrelevant when MaxQueuedActions = 0
};
```

**Why:** When `QueueTimeout > TimeSpan.Zero`, the bulkhead uses `SemaphoreSlim.WaitAsync(timeout)` to wait for a slot. When `QueueTimeout` is zero, `WaitAsync(0)` returns `false` immediately if no slot is available, regardless of `MaxQueuedActions`. If you set a queue size, also set a queue timeout. If you want instant rejection, set `MaxQueuedActions = 0` to make the intent explicit.

---

## 11. Registering AddPragmaticResilience() Multiple Times with Different Options

**Wrong:**

```csharp
// In one startup step:
services.AddPragmaticResilience(o =>
{
    o.Policies["api-a"] = new ResiliencePolicyOptions { Timeout = new() { Timeout = TimeSpan.FromSeconds(5) } };
});

// In another startup step:
services.AddPragmaticResilience(o =>
{
    o.Policies["api-b"] = new ResiliencePolicyOptions { Retry = new() { MaxRetries = 3 } };
});
```

**Runtime result:** The second `AddPragmaticResilience()` call uses `services.Configure<ResilienceOptions>()` which applies the callback additively. Both policies are registered. However, `TryAddSingleton` for `IResiliencePipelineProvider` means the provider is only registered once. This works, but the pattern is fragile -- if someone replaces the first call's policy name with the same name as the second, the last `Configure` callback wins.

**Right:**

```csharp
// Single registration with all policies
services.AddPragmaticResilience(o =>
{
    o.Policies["api-a"] = new ResiliencePolicyOptions { Timeout = new() { Timeout = TimeSpan.FromSeconds(5) } };
    o.Policies["api-b"] = new ResiliencePolicyOptions { Retry = new() { MaxRetries = 3 } };
});

// Or use AddResiliencePolicy for individual policies
services.AddPragmaticResilience();
services.AddResiliencePolicy("api-a", o => o.Timeout = new() { Timeout = TimeSpan.FromSeconds(5) });
services.AddResiliencePolicy("api-b", o => o.Retry = new() { MaxRetries = 3 });
```

**Why:** `AddPragmaticResilience()` is the main registration call. `AddResiliencePolicy(name, configure)` is the per-policy helper that adds individual policies via `services.Configure<ResilienceOptions>()`. Using `AddResiliencePolicy` after a single `AddPragmaticResilience()` call keeps the intent clear and avoids confusion about which callback runs last.

---

## Quick Reference

| Mistake | Symptom |
|---------|---------|
| Retrying business errors | Unnecessary retries, delayed failure response |
| Missing state store in builder | Compile error on `AddCircuitBreaker` |
| Timeout shorter than retry total | Retries cancelled early, fewer attempts than configured |
| No ShouldRetry filter | Programming errors retried unnecessarily |
| New state store per call | Circuit never opens, breaker is useless |
| Pessimistic timeout on async APIs | Background tasks leak resources |
| Missing OperationName | Compile error (required property) |
| FailureThreshold = 1 | Single transient failure opens circuit |
| Manual wrapping vs [ResiliencePolicy] | Verbose code, lost Result-awareness |
| Queue without QueueTimeout | Queue effectively disabled |
| Multiple AddPragmaticResilience | Fragile registration, potential policy override |
