---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Resilience exists, how its pieces fit together, and how to choose the right strategy for each situation. Read this before "
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Resilience/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Resilience exists, how its pieces fit together, and how to choose the right strategy for each situation. Read this before diving into the individual policy guides.

---

## The Problem

Distributed systems fail. HTTP calls time out, databases go down, third-party APIs return 500s, and downstream services get overwhelmed. Without resilience patterns, a single failing dependency can cascade across your entire application.

### The common approach: try/catch and hope

```csharp
public class PaymentGatewayClient(HttpClient http)
{
    public async Task<PaymentResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/charges", request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PaymentResult>(ct);
        }
        catch (HttpRequestException)
        {
            // Retry? How many times? With what delay?
            // What if the service is down for minutes?
            throw;
        }
        catch (TaskCanceledException)
        {
            // Timeout? Or did the caller cancel?
            throw;
        }
    }
}
```

One external call, and the problems are already stacking up:

- **No retry logic.** A single transient network blip causes a hard failure.
- **No timeout control.** The default `HttpClient` timeout is 100 seconds. A hung downstream service keeps the request thread blocked for nearly two minutes.
- **No circuit breaking.** If the payment gateway is down, every request still attempts the call, wasting resources and increasing latency for all users.
- **No concurrency control.** Under load, hundreds of threads pile into the failing service simultaneously.

### The Polly/manual approach: scattered, coupled, allocation-heavy

Developers often turn to Polly or hand-roll resilience logic:

```csharp
// Polly approach: works, but has tradeoffs
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));

var circuitBreaker = Policy
    .Handle<HttpRequestException>()
    .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

var timeout = Policy.TimeoutAsync(10);

var combined = Policy.WrapAsync(timeout, circuitBreaker, retryPolicy);

await combined.ExecuteAsync(ct => http.PostAsJsonAsync("/charges", request, ct), ct);
```

This works, but:

- **External dependency.** Polly pulls in transitive NuGet packages and is not AOT-safe without careful configuration.
- **Scattered configuration.** Policies are defined in code, in DI registration, and sometimes in configuration files. There is no single source of truth.
- **No SG integration.** Every DomainAction that calls an external service must manually wire its pipeline. Forget once, and that action runs without protection.
- **Result-unaware.** Polly retries on exceptions but does not understand `Result<T, E>`. A validation error (`Result.Failure`) would be retried as if it were a transient failure.

**The fundamental issue**: resilience is a cross-cutting concern that should be declared, not implemented. Every external call needs the same patterns, yet every developer implements them differently.

---

## The Solution

Pragmatic.Resilience inverts the model. You **declare** which resilience policy applies to an operation, and the framework composes the pipeline at runtime (or the source generator wires it at compile time for DomainActions).

The same payment gateway call with Pragmatic.Resilience:

```csharp
[DomainAction]
[ResiliencePolicy("payment-gateway")]
public partial class ChargeCustomerAction : DomainAction<PaymentResult>
{
    public override async Task<Result<PaymentResult, IError>> Execute(CancellationToken ct)
    {
        // This execution is wrapped by the "payment-gateway" resilience pipeline.
        // Exceptions trigger retry + circuit breaker.
        // Result failures (validation, not found) pass through unchanged.
        var response = await http.PostAsJsonAsync("/charges", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentResult>(ct);
    }
}
```

The policy is defined once in configuration:

```json
{
  "Resilience": {
    "Policies": {
      "payment-gateway": {
        "Timeout": { "Timeout": "00:00:10" },
        "Retry": { "MaxRetries": 3, "BackoffType": "Exponential", "BaseDelay": "00:00:00.200" },
        "CircuitBreaker": { "FailureThreshold": 5, "BreakDuration": "00:00:30" }
      }
    }
  }
}
```

Seven strategies, one attribute, zero manual wiring. The source generator injects `IResiliencePipelineProvider` and wraps the action's execution. The configuration can change without recompiling.

Key design properties:

- **Native, AOT-safe.** No Polly dependency. No external NuGet packages. Full AOT compatibility.
- **Result-aware.** Only exceptions trigger resilience strategies. `Result<T, E>` failures are business errors -- retrying a "not found" or "validation failed" would be wrong.
- **Zero overhead when unused.** Unknown policy names resolve to `PassthroughPipeline.Instance`, a no-op singleton. No runtime errors, no wrapping, no allocation.
- **Declarative configuration.** Policies live in `appsettings.json` or fluent code. The same policy name is reused across multiple operations.
- **Observable by default.** Every strategy emits structured logs (`[LoggerMessage]`), OpenTelemetry traces (`ActivitySource`), and metrics (`Meter`).

---

## How It Works: The Strategy Pipeline

Every resilience-protected operation flows through a deterministic pipeline. Strategies are composable and ordered by their `Order` value (ascending). Lower order means the strategy wraps more of the pipeline -- it is more "external."

```
Request
  |
  v
Fallback (Order 25) --------> Catches ANY failure below, returns alternative
  |
  v
RateLimiter (Order 50) -----> Rejects if request rate exceeded
  |
  v
Timeout (Order 100) --------> Cancels if total time exceeded
  |
  v
Hedging (Order 150) --------> Launches parallel attempts, first success wins
  |
  v
Bulkhead (Order 200) -------> Rejects if max concurrency reached
  |
  v
Circuit Breaker (Order 300) > Rejects if circuit is open
  |
  v
Retry (Order 400) ----------> Retries on transient exception
  |
  v
Your Operation
```

The order is fixed by `StrategyOrder` constants and enforced by the pipeline builder. You do not choose the order -- the framework composes them correctly regardless of the order you add them in the builder. Lower `Order` wraps more of the pipeline (more external).

### Why this order matters

**Fallback is outermost (Order 25).** It must wrap every other strategy so it can catch *any* failure below it -- a timeout, an open circuit, exhausted retries, a rate-limit or bulkhead rejection -- and return a degraded response. If it were innermost it would only ever see the raw operation's exception and would be bypassed by every other strategy.

**Rate limiter rejects first (Order 50).** Shedding excess load before spending a timeout budget, a concurrency slot, or a retry attempt is the cheapest possible rejection.

**Timeout bounds the whole inner pipeline (Order 100).** Its budget spans everything below it -- hedged attempts, the bulkhead wait, circuit-breaker probes, and the full retry loop -- so a retry storm cannot run indefinitely. (It does not bound the outer Fallback/RateLimiter, which are cheap.)

**Bulkhead runs before circuit breaker (Order 200).** Even with the circuit closed, you want to cap how many concurrent requests hit the downstream service, preventing resource exhaustion during healthy operation, not just during failures.

**Circuit breaker wraps the retry loop (Order 300).** Because it sits *outside* Retry, the breaker sees the whole retry sequence as a single call: if all retries fail, it records **one** failure (a `RetryExhaustedException`), not one per attempt. And when the circuit is already open it rejects immediately with `CircuitBrokenException`, so the inner retry loop never even runs.

**Retry is closest to the operation (Order 400).** Each retry re-invokes only your operation -- it does not re-enter the bulkhead, timeout, or circuit breaker, which already allocated their slot/budget for the whole sequence.

---

## The Seven Strategies

### Retry

Retries the operation on transient exceptions with configurable backoff and jitter.

**When to use:** Transient failures -- network blips, HTTP 503 from a temporarily overloaded service, database connection timeouts.

**When NOT to use:** Permanent failures (authentication errors, 404s, validation failures). Retrying these wastes resources and delays the error response.

```csharp
new RetryOptions
{
    MaxRetries = 3,                          // 0 = no retries, max 100
    BaseDelay = TimeSpan.FromMilliseconds(200),
    BackoffType = BackoffType.Exponential,   // Constant, Linear, or Exponential
    MaxDelay = TimeSpan.FromSeconds(30),     // Cap to prevent unbounded growth
    UseJitter = true,                        // Decorrelated jitter (default: true)
    ShouldRetry = ex => ex is HttpRequestException or TimeoutException
}
```

**Backoff formulas:**
- **Constant:** `baseDelay` (same delay every time)
- **Linear:** `baseDelay * (attempt + 1)` (200ms, 400ms, 600ms, ...)
- **Exponential:** `baseDelay * 2^attempt` (200ms, 400ms, 800ms, ...)

**Jitter:** When `UseJitter` is enabled, the computed delay is multiplied by a random factor in `[0.5, 1.5)`. This is the decorrelated jitter algorithm recommended by AWS to prevent thundering herd -- where many clients retry at the same instant after a shared failure. Thread-local `Random` avoids lock contention in high-throughput scenarios.

**Exception on exhaustion:** Throws `RetryExhaustedException` when all attempts fail. The inner exception contains the last failure encountered.

### Timeout

Cancels the operation if it exceeds the configured duration.

**When to use:** Any external call where you need a hard upper bound on latency. Network calls, database queries, third-party API calls.

**When NOT to use:** CPU-bound computation (use `Task.Run` with cancellation instead). Long-running background jobs that are expected to run for minutes.

```csharp
new TimeoutOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    TimeoutType = TimeoutType.Optimistic    // or Pessimistic
}
```

**Timeout types:**
- **Optimistic (default):** Creates a linked `CancellationToken` and cancels it after the timeout. Preferred for all operations that honor cancellation tokens (most async .NET APIs, HttpClient, EF Core, etc.).
- **Pessimistic:** Races `Task.Delay` against the operation via `Task.WhenAny`. For operations that do not honor cancellation (legacy sync code wrapped in a task, third-party libraries that ignore tokens). The operation continues running in the background after timeout -- use with caution.

**Exception on timeout:** Throws `TimeoutRejectedException` with the operation name and timeout duration.

### Circuit Breaker

Opens after consecutive failures, rejects requests while open, allows a single probe after the break duration elapses.

**When to use:** External dependencies that can be fully down for extended periods. Payment gateways, email services, third-party APIs.

**When NOT to use:** Operations where every call is independent and has no relationship to previous failures (e.g., reading different files from disk).

```csharp
new CircuitBreakerOptions
{
    FailureThreshold = 5,                   // Consecutive failures before opening
    BreakDuration = TimeSpan.FromSeconds(30),
    ShouldHandle = ex => ex is not ArgumentException  // Don't count argument errors
}
```

**State machine:**

```
Closed ---[threshold failures]--> Open ---[break elapsed]--> HalfOpen
  ^                                                            |
  |                                                            |
  +----[probe succeeds]----<------<------<------<------<-------+
                                                               |
  Open <----[probe fails]----<------<------<------<------<-----+
```

- **Closed:** Normal operation. Failures are counted. Success resets the failure count.
- **Open:** All requests rejected immediately with `CircuitBrokenException`. No attempt to call the downstream service.
- **HalfOpen:** One probe request is allowed through. If it succeeds, the circuit closes. If it fails, the circuit reopens for another break duration.

**State store:** Circuit state is managed by `ICircuitBreakerStateStore`. The default `InMemoryCircuitBreakerStateStore` is thread-safe and per-process. For distributed scenarios (multiple app instances sharing circuit state), implement the interface with Redis or a database backend.

**Circuit key:** The circuit key defaults to `ResilienceContext.OperationKey` if set, otherwise `OperationName`. This means you can share one circuit across multiple operations that call the same downstream service, or isolate them with different keys.

### Bulkhead

Limits concurrent executions using `SemaphoreSlim` to prevent one operation from consuming all available threads or connections.

**When to use:** Operations that access a shared resource with limited capacity (database connection pool, external API with rate limits, shared file system).

**When NOT to use:** CPU-bound operations (use `Task.Run` with a bounded `TaskScheduler` instead). Operations that are already bounded by other mechanisms (e.g., an HTTP client with `MaxConnectionsPerServer`).

```csharp
new BulkheadOptions
{
    MaxConcurrency = 10,               // Maximum concurrent executions
    MaxQueuedActions = 5,              // Overflow queue (0 = no queue)
    QueueTimeout = TimeSpan.FromSeconds(2) // Max wait in queue
}
```

When all slots are taken and the queue is full (or disabled with `MaxQueuedActions = 0`), the request is rejected immediately with `BulkheadRejectedException`.

### Fallback

Catches exceptions and provides an alternative result. This is the "last resort" strategy for graceful degradation.

**When to use:** Operations where a degraded response is better than an error. Returning cached data when the source is unavailable, returning a default configuration when the config service is down.

**When NOT to use:** Critical operations where partial data could cause data corruption or inconsistency (financial transactions, inventory updates).

```csharp
new FallbackOptions<UserDto>
{
    FallbackAction = (ex, ctx, ct) => Task.FromResult(UserDto.Default),
    ShouldHandle = ex => ex is HttpRequestException,
    OnFallback = (ex, ctx) => logger.LogWarning("Using fallback for {Op}", ctx.OperationName)
}
```

Fallback is a generic strategy (`FallbackStrategy<TResult>`) that only activates when the result type matches. It is not configurable via `appsettings.json` because it requires a typed factory delegate -- use the fluent builder.

There is also a non-generic overload, `AddFallback(Func<Exception, CancellationToken, Task> fallbackAction)`, for **void** operations executed via the non-generic `IResiliencePipeline.ExecuteAsync` overload -- a typed `AddFallback<TResult>` never matches a void execution. Use it to run compensating work (log, enqueue, notify) when a void operation fails.

### Hedging

Launches parallel attempts and returns the result of whichever succeeds first.

**When to use:** Latency-sensitive read operations where trading extra load for lower tail latency is acceptable -- fan out to a second (or third) attempt if the first is slow, instead of waiting it out.

**When NOT to use:** Non-idempotent operations (payments, writes with side effects) -- hedging can execute the operation more than once. Also avoid for already-expensive or rate-limited downstream calls, where launching duplicate attempts adds load without proportional benefit.

```csharp
new HedgingOptions
{
    Delay = TimeSpan.FromSeconds(2),   // Delay before launching each subsequent attempt
    MaxAttempts = 2                    // Maximum parallel attempts (including the first)
}
```

**Exception on exhaustion:** Throws `HedgingExhaustedException` when all attempts fail. The inner exception carries the last underlying failure.

### Rate Limiter

Caps the number of requests allowed within a rolling time window using a sliding-log algorithm (a timestamp queue, not token-bucket or fixed-window). Rejects immediately once the window is full -- there is no queuing.

**When to use:** Protecting a downstream dependency (or your own service) from being overwhelmed by enforcing a hard request-rate ceiling -- third-party APIs with rate limits, expensive endpoints, per-tenant throttling.

**When NOT to use:** Scenarios that need burst tolerance with queuing rather than immediate rejection (use Bulkhead's queue instead), or where the caller cannot handle a rejection response.

```csharp
new RateLimiterOptions
{
    MaxRequests = 100,                 // Maximum requests allowed within the window
    Window = TimeSpan.FromMinutes(1)   // Sliding window duration
}
```

**Exception on rejection:** Throws `RateLimitRejectedException` immediately when the window is full.

---

## Composing Strategies

Strategies compose into a single pipeline. In configuration, set any strategy to `null` (or omit it) to exclude it from the pipeline.

```csharp
new ResiliencePolicyOptions
{
    Timeout = new() { Timeout = TimeSpan.FromSeconds(10) },     // Included
    Retry = new() { MaxRetries = 3 },                           // Included
    CircuitBreaker = new() { FailureThreshold = 5 },            // Included
    Bulkhead = null                                              // Excluded
}
```

Only non-null strategies are composed. A policy with only `Timeout` set produces a pipeline with exactly one strategy. A policy with nothing set produces `PassthroughPipeline.Instance` (zero overhead).

### Common compositions

| Scenario | Strategies | Why |
|----------|-----------|-----|
| External HTTP API | Timeout + Retry + CircuitBreaker | Transient failures need retry; sustained outage needs circuit breaking; timeout prevents hanging |
| Database query | Timeout + Retry(2, Constant) | Brief connection timeouts are retried; no circuit breaker because every query is independent |
| Shared resource (connection pool) | Timeout + Bulkhead | Limit concurrency to match pool size; timeout prevents queued requests from waiting forever |
| Non-critical feature (recommendations) | Timeout + Retry + Fallback | Retry transient failures; return cached/default data if all else fails |
| Critical write (payment) | Timeout + CircuitBreaker | Timeout prevents hanging; circuit breaker prevents hammering a down service; no retry because payment could be processed but response lost |

---

## Configuration

### appsettings.json structure

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
      },
      "database": {
        "Timeout": { "Timeout": "00:00:05" },
        "Retry": {
          "MaxRetries": 2,
          "BackoffType": "Constant",
          "BaseDelay": "00:00:00.100"
        }
      }
    }
  }
}
```

### Resolution order

When `IResiliencePipelineProvider.GetPipeline(name)` is called, the provider checks in this order:

1. **Fluent builder overrides** -- policies registered via `provider.AddPolicy(name, configure)` (the `Func<ResiliencePipelineBuilder, ResiliencePipelineBuilder>` overload)
2. **Policy-options overrides** -- policies registered via `provider.AddPolicy(name, ResiliencePolicyOptions)`
3. **Configuration** -- policies from `ResilienceOptions.Policies`, whether bound from `appsettings.json` or added via `AddResiliencePolicy(name, configure)`
4. **Default** -- `ResilienceOptions.Default` if set
5. **Passthrough** -- `PassthroughPipeline.Instance` (zero overhead, no wrapping)

`AddPolicy` and `MapOperation` are declared on `IResiliencePipelineRegistry` (which extends `IResiliencePipelineProvider`). Code that only injects `IResiliencePipelineProvider` can resolve pipelines but cannot register overrides -- inject `IResiliencePipelineRegistry` for that.

This means a fluent builder override always wins over a policy-options override, which always wins over configuration, and configuration always wins over the default. Unknown policy names resolve to passthrough -- no runtime errors -- and the generated host logs one warning per declared name nothing defines, at startup.

### DI registration

`AddPragmaticResilience()` registers three services:

| Service | Lifetime | Description |
|---------|----------|-------------|
| `IResiliencePipelineProvider` | Singleton | Resolves named pipelines by name or operation |
| `ICircuitBreakerStateStore` | Singleton | Default: `InMemoryCircuitBreakerStateStore` |
| `ResilienceOptions` | Singleton | Configuration via `IOptions<ResilienceOptions>` |

The provider caches built pipelines by name using `ConcurrentDictionary`. The first call to `GetPipeline("name")` builds the pipeline; subsequent calls return the cached instance.

### Fluent builder (no DI)

For standalone usage without dependency injection:

```csharp
var stateStore = new InMemoryCircuitBreakerStateStore();

var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(o => { o.MaxRetries = 3; o.BackoffType = BackoffType.Exponential; })
    .AddTimeout(o => o.Timeout = TimeSpan.FromSeconds(5))
    .AddCircuitBreaker(stateStore, o => { o.FailureThreshold = 5; })
    .Build();

var result = await pipeline.ExecuteAsync(
    (ctx, ct) => httpClient.GetStringAsync(url, ct),
    new ResilienceContext { OperationName = "FetchData" });
```

The builder sorts strategies by `Order` automatically. You can add them in any order -- `AddRetry` before `AddTimeout` produces the same pipeline as the reverse.

`ResiliencePipelineBuilder` exposes one `Add*` method per built-in strategy -- `AddRetry`, `AddTimeout`, `AddCircuitBreaker`, `AddBulkhead`, `AddHedging`, `AddRateLimiter` -- plus `AddFallback<TResult>(Func<CancellationToken, Task<TResult>>)` (and a `FallbackOptions<TResult>` overload) for typed results, `AddFallback(Func<Exception, CancellationToken, Task>)` for void operations, `AddStrategy` for custom strategies, and `Build`.

If no strategies are added, `Build()` returns `PassthroughPipeline.Instance`.

---

## Custom Strategies

Implement `IResilienceStrategy` to create strategies that are not built in:

```csharp
public class RateLimitStrategy : IResilienceStrategy
{
    public int Order => 150; // Between Timeout (100) and Bulkhead (200)

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        await AcquireTokenAsync(ct);
        return await next(context, ct);
    }
}

var pipeline = new ResiliencePipelineBuilder()
    .AddStrategy(new RateLimitStrategy())
    .AddRetry()
    .Build();
```

The `Order` property determines where your strategy sits in the pipeline. Choose a value between the built-in constants (`StrategyOrder.Timeout = 100`, `StrategyOrder.Bulkhead = 200`, etc.) to position it correctly.

---

## ResilienceContext

`ResilienceContext` carries metadata through the pipeline. Every strategy receives the same context instance.

```csharp
var context = new ResilienceContext
{
    OperationName = "ChargeCustomer",  // Required. Used in logging, metrics, tracing.
    OperationKey = "payment-api"       // Optional. Overrides OperationName for circuit key.
};
```

| Property | Type | Description |
|----------|------|-------------|
| `OperationName` | `string` (required) | Logical operation name. Used in logs, metrics, and activity names. |
| `OperationKey` | `string?` | Circuit breaker isolation key. Defaults to `OperationName` if not set. |
| `AttemptNumber` | `int` | Current retry attempt (0 = first attempt). Updated by `RetryStrategy`. |
| `TotalElapsed` | `TimeSpan` | Total elapsed time since the first attempt. Updated by the pipeline. |
| `Properties` | `IDictionary<string, object>` | Arbitrary key-value pairs for cross-strategy communication. |

Use `Properties` to pass data between custom strategies without coupling them. For example, a rate limit strategy could store the remaining token count for a logging strategy to read.

---

## Error Types

Resilience errors implement `Pragmatic.Result.Error` for integration with the Result pattern. Each error has a code, HTTP status mapping, and descriptive title.

| Error Record | Code | HTTP Status | When |
|-------------|------|-------------|------|
| `TimeoutError` | `TIMEOUT` | 504 Gateway Timeout | Operation exceeded timeout duration |
| `RetryExhaustedError` | `RETRY_EXHAUSTED` | 503 Service Unavailable | All retry attempts failed |
| `CircuitBrokenError` | `CIRCUIT_BROKEN` | 503 Service Unavailable | Circuit is open, requests rejected |
| `BulkheadRejectedError` | `BULKHEAD_REJECTED` | 429 Too Many Requests | Max concurrency exceeded |
| `HedgingExhaustedError` | `HEDGING_EXHAUSTED` | 503 Service Unavailable | All hedging attempts failed |
| `RateLimitRejectedError` | `RATE_LIMIT_REJECTED` | 429 Too Many Requests | Request rate exceeded the configured window |

Each strategy also throws a corresponding exception for pipeline-level control flow:

| Exception | Thrown By |
|-----------|----------|
| `TimeoutRejectedException` | `TimeoutStrategy` when timeout elapses |
| `RetryExhaustedException` | `RetryStrategy` when all retries fail (inner exception = last failure) |
| `CircuitBrokenException` | `CircuitBreakerStrategy` when circuit is open |
| `BulkheadRejectedException` | `BulkheadStrategy` when capacity is full |
| `HedgingExhaustedException` | `HedgingStrategy` when all parallel attempts fail (inner exception = last failure) |
| `RateLimitRejectedException` | `RateLimiterStrategy` when the sliding window is full |

The error records are for mapping to `Result<T, E>` at the action/endpoint layer. The exceptions are for pipeline-level control flow between strategies.

---

## The Result Boundary

A key design decision: **only exceptions trigger resilience strategies.** `Result<T, E>` failures pass through the pipeline unchanged.

```csharp
[DomainAction]
[ResiliencePolicy("external-api")]
public partial class FetchUserAction : DomainAction<UserDto>
{
    public override async Task<Result<UserDto, IError>> Execute(CancellationToken ct)
    {
        var user = await _userService.GetByIdAsync(Id, ct);

        // This Result failure passes through -- NOT retried.
        // "User not found" is a business outcome, not a transient failure.
        if (user is null)
            return NotFoundError.For("User", Id);

        // This exception IS retried (if HttpRequestException matches ShouldRetry).
        var profile = await http.GetFromJsonAsync<Profile>($"/profiles/{user.ExternalId}", ct);

        return UserDto.FromEntity(user, profile);
    }
}
```

Why this matters:
- **Retrying a "not found" is wrong.** The entity does not exist. Retrying will not make it appear.
- **Retrying a validation error is wrong.** The input is invalid. Sending the same invalid input again will produce the same error.
- **Retrying an `HttpRequestException` is right.** The server might have been temporarily unavailable.

This boundary is automatic. The SG wraps the `Execute` method -- if it returns a `Result.Failure`, the pipeline sees a successful execution (no exception thrown) and passes the result through. If it throws, the pipeline applies the resilience strategies.

**When a strategy gives up, the caller still gets a `Result` failure, not a raw exception.** If the pipeline ultimately exhausts (retry exhausted, circuit open, timeout, bulkhead/rate-limit rejected, hedging exhausted), the generated invoker catches the resulting exception via `ResilienceResultBridge.TryMapToError` and returns the matching typed error (`RetryExhaustedError`, `CircuitBrokenError`, `TimeoutError`, `BulkheadRejectedError`, `RateLimitRejectedError`, `HedgingExhaustedError`) as a `Result` failure -- so an HTTP endpoint built on the action automatically gets the correct status code (503, 504, or 429) without any manual catch. Exceptions unrelated to resilience still propagate normally.

---

## Observability

### Distributed tracing

`ActivitySource`: `"Pragmatic.Resilience"`

Each pipeline execution creates an activity named `Resilience.{policyName}` (where `policyName` comes from `OperationKey` or defaults to `"unknown"`). Tags include:

- `policy.name` -- the resolved policy name
- `outcome` -- `"success"`, `"retry"`, or `"exception"`
- `attempt` -- retry attempt number (if retried)

### Metrics

`Meter`: `"Pragmatic.Resilience"`

| Instrument | Type | Name | Description |
|------------|------|------|-------------|
| Pipeline duration | Histogram | `pragmatic.resilience.duration` | Execution duration in milliseconds |
| Pipeline executions | Counter | `pragmatic.resilience.executions` | Total pipeline executions |
| Retry attempts | Counter | `pragmatic.resilience.retry_attempts` | Total retry attempts |
| Circuit rejections | Counter | `pragmatic.resilience.circuit_rejections` | Requests rejected by open circuits |
| Timeouts | Counter | `pragmatic.resilience.timeouts` | Total timeout occurrences |
| Bulkhead rejections | Counter | `pragmatic.resilience.bulkhead_rejections` | Requests rejected by bulkhead |
| Hedging attempts | Counter | `pragmatic.resilience.hedging_attempts` | Total hedging attempts launched |
| Hedging successes | Counter | `pragmatic.resilience.hedging_successes` | Total hedging first-wins |
| Rate limit rejections | Counter | `pragmatic.resilience.rate_limit_rejections` | Total rate limit rejections |

### Structured logging

All log messages use `[LoggerMessage]` source-generated partial methods for zero-allocation structured logging:

| Event | Level | Message |
|-------|-------|---------|
| Retry attempt | Warning | `Retry attempt {N}/{Max} for {Op} after {Delay}ms. Error: {Msg}` |
| Timeout | Warning | `Operation {Op} timed out after {Timeout}ms` |
| Retry exhausted | Error | `All {Max} retry attempts exhausted for {Op}. Last error: {Msg}` |
| Circuit rejected | Warning | `Circuit '{Key}' rejected request -- circuit is open` |
| Circuit opened | Warning | `Circuit '{Key}' opened after {N} consecutive failures` |
| Bulkhead rejected | Warning | `Bulkhead rejected '{Op}' -- max concurrency {N} reached` |
| Fallback used | Information | `Fallback used for '{Op}'. Original error: {Msg}` |

---

## Ecosystem Integration

### Pragmatic.Actions

Annotate a `DomainAction` with `[ResiliencePolicy("name")]` to automatically wrap execution with the named pipeline. The source generator detects `Pragmatic.Resilience` in the project references, injects `IResiliencePipelineProvider`, and wraps `Execute` with the resolved pipeline. If the pipeline exhausts, the generated invoker maps the resilience exception to the corresponding typed error and returns it as a `Result` failure -- the action never lets a resilience exception escape to the caller.

```csharp
[DomainAction]
[ResiliencePolicy("payment-gateway")]
public partial class ChargeCustomerAction : DomainAction<PaymentResult>
{
    public override async Task<Result<PaymentResult, IError>> Execute(CancellationToken ct)
    {
        // Wrapped by the "payment-gateway" pipeline
    }
}
```

### Pragmatic.Result

Error records (`TimeoutError`, `RetryExhaustedError`, `CircuitBrokenError`, `BulkheadRejectedError`, `HedgingExhaustedError`, `RateLimitRejectedError`) extend `Pragmatic.Result.Error` with HTTP status code mappings. For `[ResiliencePolicy]`-annotated actions, the generated invoker performs this mapping automatically via `ResilienceResultBridge.TryMapToError`, so the endpoint gets the correct HTTP response (504, 503, or 429) from the `Result` failure without catching anything itself.

### Pragmatic.Composition

When `Pragmatic.Resilience` is referenced in the project, the source generator auto-registers `AddPragmaticResilience()` with default settings via `FeatureDetector`. No manual registration is needed for basic usage. For custom policies, call `AddPragmaticResilience(options => ...)` in your `IStartupStep`.

---

## See Also

- [Getting Started](/modules/resilience/getting-started/) -- Install and configure your first resilience policy
- [Policies](/modules/resilience/policies/) -- Detailed reference for each strategy with all options
- [Common Mistakes](/modules/resilience/common-mistakes/) -- Frequent errors and how to avoid them
- [Troubleshooting](/modules/resilience/troubleshooting/) -- Problem/solution guide for runtime issues
