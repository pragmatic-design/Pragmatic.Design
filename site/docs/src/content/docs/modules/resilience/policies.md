---
title: "Resilience Policies"
description: "Detailed reference for all resilience strategies in Pragmatic.Resilience. Strategies are composable and ordered by their `Order` value (ascending). Lower order "
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Resilience/docs/policies.md
sidebar:
  order: 3
---
Detailed reference for all resilience strategies in Pragmatic.Resilience. Strategies are composable and ordered by their `Order` value (ascending). Lower order means the strategy wraps more of the pipeline (more external).

## Strategy Execution Order

```
Request
  |
  v
Fallback (Order 25) -----> catches ANY failure below, returns alternative
  |
  v
RateLimiter (Order 50) --> rejects if request rate exceeded
  |
  v
Timeout (Order 100) -----> cancels if total time exceeded
  |
  v
Hedging (Order 150) -----> parallel attempts, first success wins
  |
  v
Bulkhead (Order 200) ----> rejects if max concurrency reached
  |
  v
CircuitBreaker (Order 300) -> rejects if circuit is open (wraps the whole retry loop)
  |
  v
Retry (Order 400) -------> retries on transient exception
  |
  v
Operation
```

Lower `Order` wraps more of the pipeline (more external). Because CircuitBreaker (300) sits outside Retry (400), an exhausted retry sequence counts as **one** circuit-breaker failure, not one per attempt.

All strategies implement `IResilienceStrategy`:

```csharp
public interface IResilienceStrategy
{
    int Order { get; }

    Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct);
}
```

## Retry Strategy

Retries on exceptions with configurable backoff and jitter.

### RetryOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxRetries` | `int` | `3` | Maximum retry attempts (0 = no retries, max 100) |
| `BaseDelay` | `TimeSpan` | `200ms` | Base delay between retries |
| `BackoffType` | `BackoffType` | `Exponential` | `Constant`, `Linear`, or `Exponential` |
| `MaxDelay` | `TimeSpan` | `30s` | Upper bound on delay (prevents unbounded growth) |
| `UseJitter` | `bool` | `true` | Decorrelated jitter to prevent thundering herd |
| `ShouldRetry` | `Func<Exception, bool>?` | `null` (all) | Predicate to filter which exceptions trigger retry |

### Backoff Formulas

- **Constant**: `baseDelay`
- **Linear**: `baseDelay * (attempt + 1)`
- **Exponential**: `baseDelay * 2^attempt`

When `UseJitter` is enabled, the computed delay is multiplied by a random factor in `[0.5, 1.5)` (decorrelated jitter, per the AWS recommendation). Thread-local `Random` avoids lock contention in high-throughput scenarios.

### Exception

Throws `RetryExhaustedException` when all attempts are exhausted. The inner exception contains the last failure.

### Example

```csharp
services.AddResiliencePolicy("transient-calls", o =>
{
    o.Retry = new RetryOptions
    {
        MaxRetries = 5,
        BaseDelay = TimeSpan.FromMilliseconds(100),
        BackoffType = BackoffType.Exponential,
        MaxDelay = TimeSpan.FromSeconds(10),
        UseJitter = true,
        ShouldRetry = ex => ex is HttpRequestException or TimeoutException
    };
});
```

## Timeout Strategy

Cancels the operation if it exceeds the configured duration.

### TimeoutOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Timeout` | `TimeSpan` | `30s` | Maximum allowed duration |
| `TimeoutType` | `TimeoutType` | `Optimistic` | Cancellation approach |

### Timeout Types

- **Optimistic** -- Creates a linked `CancellationToken` and cancels it after the timeout. Preferred for operations that honor cancellation tokens (most async .NET APIs).
- **Pessimistic** -- Races `Task.Delay` against the operation via `Task.WhenAny`. For operations that do not honor cancellation (e.g., legacy synchronous code wrapped in a task). The operation may continue running in the background after timeout.

### Exception

Throws `TimeoutRejectedException` when the timeout is exceeded.

### Example

```csharp
o.Timeout = new TimeoutOptions
{
    Timeout = TimeSpan.FromSeconds(5),
    TimeoutType = TimeoutType.Optimistic
};
```

## Circuit Breaker Strategy

Opens after consecutive failures, rejects requests while open, allows a probe after the break duration elapses.

### CircuitBreakerOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `FailureThreshold` | `int` | `5` | Consecutive failures before opening |
| `BreakDuration` | `TimeSpan` | `30s` | How long the circuit stays open |
| `ShouldHandle` | `Func<Exception, bool>?` | `null` (all) | Predicate to filter which exceptions count as failures |

### State Machine

```
Closed ---[threshold failures]--> Open ---[break elapsed]--> HalfOpen
  ^                                                            |
  |                                                            |
  +----[probe succeeds]----<------<------<------<------<-------+
                                                               |
  Open <----[probe fails]----<------<------<------<------<-----+
```

- **Closed**: normal operation. Failures are counted.
- **Open**: all requests rejected immediately with `CircuitBrokenException`.
- **HalfOpen**: one probe request allowed through. If it succeeds, circuit closes. If it fails, circuit reopens.

### State Store

Circuit breaker state is managed by `ICircuitBreakerStateStore`. The default `InMemoryCircuitBreakerStateStore` is thread-safe and per-process. For distributed scenarios (multiple instances sharing circuit state), implement the interface with Redis or a database backend.

### Exception

Throws `CircuitBrokenException` when the circuit is open and a request is rejected.

### Example

```csharp
o.CircuitBreaker = new CircuitBreakerOptions
{
    FailureThreshold = 3,
    BreakDuration = TimeSpan.FromSeconds(60),
    ShouldHandle = ex => ex is not ArgumentException // Don't count argument errors
};
```

## Bulkhead Strategy

Limits concurrent executions using `SemaphoreSlim`.

### BulkheadOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxConcurrency` | `int` | `10` | Maximum concurrent executions |
| `MaxQueuedActions` | `int` | `0` | Overflow queue size (0 = no queue) |
| `QueueTimeout` | `TimeSpan` | `TimeSpan.Zero` | Maximum wait time in queue |

When all slots are taken and the queue is full (or disabled), the request is rejected immediately with `BulkheadRejectedException`.

### Example

```csharp
o.Bulkhead = new BulkheadOptions
{
    MaxConcurrency = 5,
    MaxQueuedActions = 10,
    QueueTimeout = TimeSpan.FromSeconds(2)
};
```

## Fallback Strategy

Catches exceptions and provides an alternative result. This is a generic strategy (`FallbackStrategy<TResult>`) that only activates when the result type matches.

### FallbackOptions\<TResult\>

| Option | Type | Description |
|--------|------|-------------|
| `FallbackAction` | `Func<Exception, ResilienceContext, CancellationToken, Task<TResult>>` | Factory that produces the fallback value (required) |
| `ShouldHandle` | `Func<Exception, bool>?` | Predicate to filter which exceptions trigger the fallback |
| `OnFallback` | `Action<Exception, ResilienceContext>?` | Callback invoked when fallback is used (for logging/metrics) |

Fallback is not configurable via `ResiliencePolicyOptions` (JSON/DI). It must be added via the fluent builder because it requires a typed factory delegate.

### Example

```csharp
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry()
    .AddStrategy(new FallbackStrategy<UserDto>(new FallbackOptions<UserDto>
    {
        FallbackAction = (ex, ctx, ct) => Task.FromResult(UserDto.Default),
        ShouldHandle = ex => ex is HttpRequestException,
        OnFallback = (ex, ctx) => logger.LogWarning("Using fallback for {Op}", ctx.OperationName)
    }))
    .Build();
```

### Void Operations

`AddFallback<TResult>` never intercepts a **void** operation (one executed via the non-generic `IResiliencePipeline.ExecuteAsync` overload), because it only matches on the typed result. Use the dedicated void overload to run compensating work when a void operation fails:

```csharp
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry()
    .AddFallback((ex, ct) =>
    {
        logger.LogWarning(ex, "Void operation failed; running compensating work");
        return compensationQueue.EnqueueAsync(ex.Message, ct);
    })
    .Build();
```

## Hedging Strategy

Launches parallel attempts and returns the result of whichever succeeds first.

### HedgingOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Delay` | `TimeSpan` | `2s` | Delay before launching each subsequent hedged attempt |
| `MaxAttempts` | `int` | `2` | Maximum number of parallel attempts (including the first) |

### Exception

Throws `HedgingExhaustedException` when all attempts fail. The inner exception carries the last underlying failure.

### Example

```json
{
  "Resilience": {
    "Policies": {
      "hedged-read": {
        "Hedging": { "Delay": "00:00:00.200", "MaxAttempts": 3 }
      }
    }
  }
}
```

```csharp
var pipeline = new ResiliencePipelineBuilder()
    .AddHedging(o =>
    {
        o.MaxAttempts = 3;
        o.Delay = TimeSpan.FromMilliseconds(200);
    })
    .Build();
```

## Rate Limiter Strategy

Caps the number of requests allowed within a rolling time window using a sliding-log algorithm (a timestamp queue, not token-bucket or fixed-window). Rejects immediately once the window is full -- there is no queuing.

### RateLimiterOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `MaxRequests` | `int` | `100` | Maximum number of requests allowed within the window |
| `Window` | `TimeSpan` | `1 minute` | Sliding window duration |

### Exception

Throws `RateLimitRejectedException` immediately when the window is full.

### Example

```json
{
  "Resilience": {
    "Policies": {
      "throttled-api": {
        "RateLimiter": { "MaxRequests": 100, "Window": "00:01:00" }
      }
    }
  }
}
```

```csharp
var pipeline = new ResiliencePipelineBuilder()
    .AddRateLimiter(o =>
    {
        o.MaxRequests = 100;
        o.Window = TimeSpan.FromMinutes(1);
    })
    .Build();
```

## Custom Strategies

Implement `IResilienceStrategy` to create custom strategies:

```csharp
public class RateLimitStrategy : IResilienceStrategy
{
    public int Order => 150; // Between Timeout (100) and Bulkhead (200)

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<ResilienceContext, CancellationToken, Task<TResult>> next,
        ResilienceContext context,
        CancellationToken ct)
    {
        // Custom logic here
        return await next(context, ct);
    }
}

var pipeline = new ResiliencePipelineBuilder()
    .AddStrategy(new RateLimitStrategy())
    .AddRetry()
    .Build();
```

## ResilienceContext

Cross-strategy communication without coupling strategies to each other:

```csharp
var context = new ResilienceContext
{
    OperationName = "FetchUserData"
};
```

The `OperationName` is used in logging, metrics, and tracing.

## Composing Policies

A `ResiliencePolicyOptions` composes multiple strategies into a single pipeline:

```csharp
new ResiliencePolicyOptions
{
    Timeout = new() { ... },        // null = no timeout
    Bulkhead = new() { ... },       // null = no bulkhead
    CircuitBreaker = new() { ... }, // null = no circuit breaker
    Retry = new() { ... },          // null = no retry
    Hedging = new() { ... },        // null = no hedging
    RateLimiter = new() { ... }     // null = no rate limiting
}
```

Set any strategy to `null` to exclude it from the pipeline. Only non-null strategies are composed. `Hedging` and `RateLimiter` are bindable from `appsettings.json` like the other strategies (see the Hedging and Rate Limiter reference sections above); `Fallback` is the only strategy that is not, since it requires a typed factory delegate.

## Observability

### Distributed Tracing

`ActivitySource`: `"Pragmatic.Resilience"`. Each pipeline execution creates an activity `Resilience.{policyName}` with tags: `policy.name`, `outcome`, `attempt`.

### Metrics

`Meter`: `"Pragmatic.Resilience"`.

| Instrument | Type | Name |
|------------|------|------|
| Pipeline duration | Histogram | `pragmatic.resilience.duration` |
| Pipeline executions | Counter | `pragmatic.resilience.executions` |
| Retry attempts | Counter | `pragmatic.resilience.retry_attempts` |
| Circuit rejections | Counter | `pragmatic.resilience.circuit_rejections` |
| Timeouts | Counter | `pragmatic.resilience.timeouts` |
| Bulkhead rejections | Counter | `pragmatic.resilience.bulkhead_rejections` |
| Hedging attempts | Counter | `pragmatic.resilience.hedging_attempts` |
| Hedging successes | Counter | `pragmatic.resilience.hedging_successes` |
| Rate limit rejections | Counter | `pragmatic.resilience.rate_limit_rejections` |

### Structured Logging

All log messages use `[LoggerMessage]` source-generated partial methods:

| Level | Message |
|-------|---------|
| Warning | `Retry attempt {N}/{Max} for {Op} after {Delay}ms` |
| Warning | `Operation {Op} timed out after {Timeout}ms` |
| Error | `All {Max} retry attempts exhausted for {Op}` |
| Warning | `Circuit '{Key}' rejected request -- circuit is open` |
| Warning | `Circuit '{Key}' opened after {N} consecutive failures` |
| Warning | `Bulkhead rejected '{Op}' -- max concurrency {N} reached` |
| Information | `Fallback used for '{Op}'. Original error: {Msg}` |
