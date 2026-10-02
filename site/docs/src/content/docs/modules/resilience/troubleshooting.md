---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Resilience. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Resilience/docs/troubleshooting.md
sidebar:
  order: 5
---
Practical problem/solution guide for Pragmatic.Resilience. Each section covers a common issue, the likely causes, and the fix.

---

## Retries Not Happening

Your pipeline has retry configured, but exceptions are not retried -- they propagate immediately.

### Checklist

1. **Is `MaxRetries` set to 0?** A value of 0 disables retries entirely. The default is 3.

2. **Is `ShouldRetry` filtering out the exception?** If you configured a `ShouldRetry` predicate, verify it returns `true` for the exception type you are seeing:

   ```csharp
   o.Retry = new RetryOptions
   {
       ShouldRetry = ex => ex is HttpRequestException  // Only retries HttpRequestException
   };
   ```

   An `IOException` or `TimeoutRejectedException` would not be retried with this filter.

3. **Is the exception `OperationCanceledException` from external cancellation?** Retry explicitly does not retry cancellation from the caller's `CancellationToken`. This is by design -- the caller asked to cancel.

4. **Is the operation returning a `Result` failure instead of throwing?** Resilience strategies only trigger on exceptions. If your code returns a failed `Result` instead of throwing, the pipeline sees a successful execution. This is by design for business errors.

5. **Is the pipeline actually applied?** When using `[ResiliencePolicy("name")]`, verify the policy name matches a configured policy. Unknown names resolve to `PassthroughPipeline`, which does nothing.

---

## Circuit Breaker Never Opens

The downstream service is clearly failing, but the circuit stays closed and requests keep going through.

### Checklist

1. **Are you using a shared state store?** If using the fluent builder, ensure the `InMemoryCircuitBreakerStateStore` instance is shared across calls (static field, DI singleton). A new instance per call has no accumulated failure history.

2. **Is `ShouldHandle` filtering out the failures?** If the circuit breaker's `ShouldHandle` predicate does not match the exception type, failures are not counted:

   ```csharp
   o.CircuitBreaker = new CircuitBreakerOptions
   {
       ShouldHandle = ex => ex is HttpRequestException  // IOException not counted
   };
   ```

3. **Is `FailureThreshold` too high?** The default is 5 consecutive failures. If errors are intermixed with successes, the consecutive count resets on each success and never reaches the threshold.

4. **Is `OperationCanceledException` being thrown?** External cancellation (caller's token) is not counted as a circuit breaker failure. This is by design -- a cancelled request does not indicate a downstream failure.

5. **Are multiple circuits being created for the same service?** The circuit key defaults to `OperationKey ?? OperationName`. If two operations call the same service but use different operation names (and no shared `OperationKey`), they maintain separate circuits. Set `OperationKey` to the same value for operations that should share a circuit:

   ```csharp
   new ResilienceContext { OperationName = "GetUser", OperationKey = "user-service" }
   new ResilienceContext { OperationName = "UpdateUser", OperationKey = "user-service" }
   ```

---

## Circuit Breaker Opens Too Aggressively

The circuit opens after what seems like a small number of failures.

### Possible Causes

**Low failure threshold.** With `FailureThreshold = 1`, a single transient exception opens the circuit. Increase to 3-5 for typical external services.

**Long break duration.** With `BreakDuration = TimeSpan.FromMinutes(5)`, one bad period locks out the service for 5 minutes. Start with 15-30 seconds and adjust based on observed recovery times.

**No ShouldHandle filter.** Without a filter, every exception counts -- including `ArgumentException`, `InvalidOperationException`, or other programming errors that are not transient.

---

## Timeout Fires But Operation Continues Running

After a `TimeoutRejectedException`, the operation keeps running in the background (visible via logs or resource usage).

### Cause

You are using `TimeoutType.Pessimistic`. Pessimistic timeout races `Task.Delay` against the operation via `Task.WhenAny`. When the delay wins, the pipeline throws `TimeoutRejectedException`, but the operation task is not truly cancelled -- it continues until it completes or the process exits.

### Fix

Switch to `TimeoutType.Optimistic` (the default). Optimistic timeout creates a linked `CancellationToken` and cancels it after the duration. Operations that honor cancellation tokens will cancel cooperatively.

Use pessimistic timeout only for operations that genuinely do not support cancellation (legacy synchronous code, third-party libraries that ignore tokens).

---

## Pipeline Returns PassthroughPipeline (No Resilience Applied)

`GetPipeline("name")` returns `PassthroughPipeline.Instance`, and your operation runs without any resilience wrapping.

### Checklist

1. **Does the policy name exist in configuration?** Check `appsettings.json` under `Resilience:Policies`. Policy names are case-insensitive.

2. **Is `AddPragmaticResilience()` called?** Without DI registration, there is no `IResiliencePipelineProvider` and no configuration binding.

3. **Is the configuration section named correctly?** The root key is `"Resilience"`, not `"ResilienceOptions"` or `"Pragmatic:Resilience"`:

   ```json
   {
     "Resilience": {
       "Policies": { "my-policy": { ... } }
     }
   }
   ```

4. **Is there a default policy?** If no named policy matches and `Resilience:Default` is not set, the provider returns `PassthroughPipeline`. Set a default for a baseline timeout:

   ```json
   {
     "Resilience": {
       "Default": {
         "Timeout": { "Timeout": "00:00:30" }
       }
     }
   }
   ```

5. **Was a fluent override cleared?** `AddPolicy()` on the provider takes precedence over configuration. If a fluent override was registered and then removed, the cache may still hold the old pipeline. Fluent overrides invalidate the cache, but race conditions during startup could cause stale entries.

---

## BulkheadRejectedException When Load Is Low

Requests are rejected by the bulkhead even though overall load is light.

### Possible Causes

**MaxConcurrency is too low.** The default is 10. If your operation takes 1 second and you have 15 concurrent users, 5 requests are rejected. Increase `MaxConcurrency` to match your expected concurrency.

**Queue is disabled.** With `MaxQueuedActions = 0` (the default), any request that cannot acquire a semaphore slot immediately is rejected. Set `MaxQueuedActions > 0` and `QueueTimeout > TimeSpan.Zero` to queue overflow.

**Semaphore leak.** If an operation throws before the `finally` block releases the semaphore (should not happen with the built-in strategy, but possible with custom strategies), slots are leaked and never recovered. The `BulkheadStrategy` uses try/finally to prevent this.

**Shared bulkhead across unrelated operations.** If multiple operations use the same policy name but have different concurrency requirements, they share the same `SemaphoreSlim`. A burst of requests to one operation starves the other.

---

## [ResiliencePolicy] Attribute Has No Effect

You annotated a `DomainAction` with `[ResiliencePolicy("name")]`, but the action runs without resilience.

### Checklist

1. **Is `Pragmatic.Resilience` referenced in the project?** The source generator detects `Pragmatic.Resilience` via `FeatureDetector`. Without the package reference, the SG does not generate resilience wrapping.

2. **Is the source generator referenced?** Verify your `.csproj` has:

   ```xml
   <ProjectReference Include="...\Pragmatic.SourceGenerator.csproj"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```

3. **Is the class `partial`?** The SG generates code into a partial class. Without `partial`, the generated code cannot merge.

4. **Is the class a `DomainAction` or a `Mutation`?** The `[ResiliencePolicy]` attribute has effect on those two. A plain class with `[ResiliencePolicy]` is ignored. A mutation nested in an operation that holds the same unit of work runs once: it stages its writes, and the retry belongs to the operation that commits.

5. **Does the policy name exist?** If the name does not match any configured policy and there is no default, the pipeline is `PassthroughPipeline` (no-op). The startup log says so: a Warning `Resilience policy '<name>' is declared by <action> and nothing defines it`.

---

## Configuration Not Binding from appsettings.json

Policies defined in `appsettings.json` are ignored, and all pipelines use defaults or passthrough.

### Checklist

1. **Is the JSON path correct?** The configuration binds from `"Resilience"` at the root level:

   ```json
   {
     "Resilience": {
       "Policies": {
         "my-policy": { ... }
       }
     }
   }
   ```

   Not `"Pragmatic:Resilience"` or `"ResilienceOptions"`.

2. **Are TimeSpan values formatted correctly?** Use the `"hh:mm:ss"` or `"hh:mm:ss.fff"` format:

   ```json
   "Timeout": "00:00:10"         // 10 seconds
   "BaseDelay": "00:00:00.200"   // 200 milliseconds
   "BreakDuration": "00:00:30"   // 30 seconds
   ```

   Not `"10s"` or `"200ms"` -- those are not valid `TimeSpan` formats for JSON deserialization.

3. **Is `AddPragmaticResilience()` called?** This registers the `IOptions<ResilienceOptions>` binding. Without it, the configuration section is never read.

4. **Are enum values spelled correctly?** `BackoffType` values are `"Constant"`, `"Linear"`, `"Exponential"`. `TimeoutType` values are `"Optimistic"`, `"Pessimistic"`. These are case-insensitive.

---

## FAQ

### Can I use Pragmatic.Resilience without DomainActions?

Yes. Inject `IResiliencePipelineProvider` into any service and call `GetPipeline("name")`. You can also use `ResiliencePipelineBuilder` directly without DI at all.

### Can I share a circuit breaker across multiple operations?

Yes. Set `OperationKey` on the `ResilienceContext` to the same value for operations that call the same downstream service. The circuit key defaults to `OperationKey ?? OperationName`.

### Does the retry strategy retry on OperationCanceledException?

No. If the caller's `CancellationToken` is cancelled, the retry strategy propagates the exception immediately. This is by design -- the caller explicitly asked to cancel.

### Can I use Fallback with DI configuration?

No. `FallbackStrategy<TResult>` requires a typed factory delegate (`Func<Exception, ResilienceContext, CancellationToken, Task<TResult>>`), which cannot be expressed in JSON. Use the fluent builder (`ResiliencePipelineBuilder.AddStrategy(new FallbackStrategy<T>(...))`) for fallback.

### What happens if I reference a policy name that does not exist?

`GetPipeline("nonexistent")` returns `PassthroughPipeline.Instance` -- a no-op singleton that executes the operation directly with zero overhead. No exception: this is intentional to allow gradual adoption — annotate actions with `[ResiliencePolicy("name")]` before configuring the policy, and the action runs unprotected until it is configured. But not silent either: at startup the generated host logs one **Warning** per declared name that no configuration defines and no `Resilience:Default` covers, naming the policy and the actions that declare it (`UndefinedResiliencePolicies`). A policy registered fluently after the pipeline is configured is reported there, and then used.

### How do I test my resilience configuration?

Build the pipeline and execute a failing operation:

```csharp
var stateStore = new InMemoryCircuitBreakerStateStore();
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(o => { o.MaxRetries = 2; o.BaseDelay = TimeSpan.FromMilliseconds(10); })
    .Build();

var attempt = 0;
await Assert.ThrowsAsync<RetryExhaustedException>(() =>
    pipeline.ExecuteAsync<string>((ctx, ct) =>
    {
        attempt++;
        throw new HttpRequestException("Simulated failure");
    }, new ResilienceContext { OperationName = "Test" }));

Assert.Equal(3, attempt); // 1 initial + 2 retries
```

### How do I implement a distributed circuit breaker?

Implement `ICircuitBreakerStateStore` with a Redis or database backend. Register it before `AddPragmaticResilience()`:

```csharp
services.AddSingleton<ICircuitBreakerStateStore, RedisCircuitBreakerStateStore>();
services.AddPragmaticResilience();  // Uses TryAddSingleton, so your registration wins
```

The interface methods (`GetSnapshotAsync`, `RecordSuccessAsync`, `RecordFailureAsync`, `TransitionToAsync`, `TryTransitionToHalfOpenAsync`) are all async to support distributed stores. `TryTransitionToHalfOpenAsync(circuitKey, now, ct)` atomically transitions the circuit from Open to HalfOpen once the break duration has elapsed, returning `true` only for the caller that won the race -- required so concurrent callers don't all send a probe request simultaneously.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Concepts Guide**: See [concepts.md](/modules/resilience/concepts/) for architecture and strategy composition.
- **Policy Reference**: See [policies.md](/modules/resilience/policies/) for all strategy options and examples.
- **Common Mistakes**: See [common-mistakes.md](/modules/resilience/common-mistakes/) for frequent errors with Wrong/Right patterns.
