---
name: pragmatic-use-resilience
description: Use when calls to external systems (HTTP, databases, queues) need retries, timeouts, circuit breaking or rate limiting (Pragmatic.Resilience, [ResiliencePolicy] on actions and handlers, named pipelines, no Polly).
---

# Pragmatic Use Resilience

**Covers:** Resilience with Pragmatic.Resilience, no Polly: a named [ResiliencePolicy] on an action or mutation gets retry, circuit breaker, timeout, hedging, rate limiter and bulkhead from configuration; any call can run through a named pipeline. Exceptions retry, Result failures pass through.

Declare a **policy name**; the generator composes the pipeline. It distinguishes exceptions
(retry/break) from `Result` failures (validation, not-found); the latter pass through unchanged, so
you never retry a business error. No Polly dependency.

⚠️ **"Exception" means every exception.** With no `ShouldRetry` the retry strategy retries anything
thrown (a `NullReferenceException` from a bug included) three more times, with back-off. Narrow it in
code for any policy in front of something that can fail for reasons other than the network (see
*Defining the policy*).

## When to use

- Wrapping outbound calls (payment gateway, third-party API, flaky dependency).
- You want declarative, composed resilience instead of hand-rolled retry loops.

## Packages

```xml
<PackageReference Include="Pragmatic.Resilience" Version="1.0.0-alpha.1" />
```

On the library that declares the policy. The host wires it because a `[ResiliencePolicy]` is
declared, not because the package is referenced; it needs no reference of its own.

## Core pattern

```csharp
using Pragmatic.Resilience.Attributes;

[DomainAction]
[ResiliencePolicy("payment-gateway")]
public partial class ChargeCustomerAction : DomainAction<PaymentResult>
{
    private IPaymentGateway _gateway = null!;           // your client, injected by the generator
    public required ChargeRequest Request { get; init; }

    public override async Task<Result<PaymentResult, IError>> Execute(CancellationToken ct = default)
        // wrapped by the "payment-gateway" pipeline: an exception here is retried / breaks the circuit
        => await _gateway.ChargeAsync(Request, ct);
}
```

`[ResiliencePolicy]` works on a `[DomainAction]` and on a `[Mutation]`. On a mutation the pipeline wraps
the **whole** invocation, save included, and every retry starts from a unit of work that has forgotten
the failed attempt and reads the row again, so nothing the first attempt tracked is written twice.
⚠️ Only where the mutation owns its commit: nested in an operation that holds the same unit of work it
stages its writes and runs once, and the retry belongs to the operation that saves. ⚠️ A retry re-runs
the body: an effect outside the database (an HTTP call, a message sent directly) happens again. When the pipeline gives up it does
not throw: the invoker returns a typed failure (`RetryExhaustedError`, `CircuitBrokenError`,
`TimeoutError`, `BulkheadRejectedError`, `RateLimitRejectedError`, `HedgingExhaustedError`), so an
endpoint answers 503, 504 or 429 without a `catch`. An empty name is **PRAG0420**.

## Defining the policy

The attribute names a policy; the host binds the `Resilience` section of configuration, so the
definition usually lives in `appsettings.json`:

```json
{
  "Resilience": {
    "Default":  { "Timeout": { "Timeout": "00:00:30" } },
    "Policies": {
      "payment-gateway": {
        "Timeout":        { "Timeout": "00:00:10" },
        "Retry":          { "MaxRetries": 3, "BaseDelay": "00:00:00.200", "BackoffType": "Exponential" },
        "CircuitBreaker": { "FailureThreshold": 5, "BreakDuration": "00:00:30" }
      }
    }
  }
}
```

In code, from an `IStartupStep`, the only place a predicate can be set, since configuration cannot
carry a delegate:

```csharp
services.AddResiliencePolicy("payment-gateway", o =>
{
    o.Retry = new() { MaxRetries = 3, ShouldRetry = ex => ex is HttpRequestException or TimeoutRejectedException };
    o.CircuitBreaker = new() { FailureThreshold = 5, ShouldHandle = ex => ex is HttpRequestException };
});
```

| Strategy | Options (default) | Order (outermost first) |
|---|---|---|
| Fallback | builder only (below) | 1 |
| RateLimiter | `MaxRequests` (100) per `Window` (1 min) | 2 |
| Timeout | `Timeout` (30 s), `TimeoutType` `Optimistic` \| `Pessimistic` | 3 |
| Hedging | `MaxAttempts` (2), `Delay` (2 s) | 4 |
| Bulkhead | `MaxConcurrency` (10), `MaxQueuedActions` (0), `QueueTimeout` | 5 |
| CircuitBreaker | `FailureThreshold` (5), `BreakDuration` (30 s), `ShouldHandle` | 6 |
| Retry | `MaxRetries` (3), `BaseDelay` (200 ms), `BackoffType` `Constant` \| `Linear` \| `Exponential`, `MaxDelay` (30 s), `UseJitter` (true), `ShouldRetry` | 7, innermost |

The order is fixed, whatever order you configure in. Consequences worth knowing:

- **Timeout wraps the retries**: 10 s is the budget for all attempts together, not per attempt.
- ⚠️ `Optimistic` (the default) cancels the token and relies on the callee honouring it; a call that
  ignores the token runs to its end. `Pessimistic` returns on time and leaves the call running in the
  background.
- ⚠️ **Hedging runs the body again, in parallel**, when the first attempt is slow. Only for reads and
  idempotent calls; on a write it is a duplicate.
- ⚠️ **A circuit is keyed by the operation, not by the policy.** Two actions sharing `payment-gateway`
  have two circuits: the gateway failing under one does not open the other's. And the state lives in
  the process (`InMemoryCircuitBreakerStateStore`, the only shipped `ICircuitBreakerStateStore`): each
  instance learns about the outage on its own.

**Fallback** takes a typed delegate, so it is not an option: register it on the registry with the
fluent builder: `IResiliencePipelineRegistry.AddPolicy(name, b => b.AddRetry(…).AddFallback<T>(ct => …))`,
or `AddFallback((ex, ct) => …)` for a void operation (it runs compensating work, it cannot supply a
value). A typed fallback only intercepts executions of that exact result type.

## Outside an operation: run a call through a pipeline

A service, a handler, a job step (anything that is not an action or a mutation) asks for the
pipeline by name and gets a `Result` back instead of a resilience exception:

```csharp
public sealed class ExchangeRateClient(HttpClient http, IResiliencePipelineProvider pipelines)
{
    public Task<Result<Rate, IError>> GetAsync(string pair, CancellationToken ct)
        => pipelines.GetPipeline("exchange-rates")
            .ExecuteAsResultAsync(async token => (await http.GetFromJsonAsync<Rate>($"rates/{pair}", token))!, ct);
}
```

`ExecuteAsResultAsync` (`Pragmatic.Resilience.Bridge`) maps the six give-up exceptions to the typed
errors above; any other exception still propagates. ⚠️ `GetPipeline` resolves a name nothing defines
to `Default`, and without one to a passthrough: the same silent no-op as an undefined
`[ResiliencePolicy]`, without the start-up warning.
⚠️ The circuit key there is the result type's name (`Rate`) unless you call `ExecuteAsync` with a
`ResilienceContext { OperationName = …, OperationKey = … }` of your own; set a key per downstream
dependency.

Outside a Pragmatic host, register the provider with `services.AddPragmaticResilience(configuration)`
(binds `Resilience`) or `AddPragmaticResilience(o => …)`.

## Observing it

Meter and ActivitySource `Pragmatic.Resilience`: `pragmatic.resilience.duration`, `.executions`,
`.retry_attempts`, `.circuit_rejections`, `.timeouts`, `.bulkhead_rejections`,
`.hedging_attempts`/`.hedging_successes`, `.rate_limit_rejections`. Add the meter to OpenTelemetry;
retries that always succeed on the second attempt are a fault the users never see and the dependency's
owner should.

## `[Retry]`, `[Timeout]`, `[CircuitBreaker]` are for jobs and message handlers

The same package ships three per-class attributes, and they are **not** an inline form of a policy.
They are read by the engine that owns the class: on a `[Job]`/`[RecurringJob]` a retry is a durable
reschedule (`pragmatic-use-jobs`), on a `[MessageHandler]` a redelivery (`pragmatic-use-messaging`).
They carry no defaults: a property you leave out is decided by that engine. `[CircuitBreaker]` is read
on a `[MessageHandler]` only. ⚠️ On a `[DomainAction]`, a `[Mutation]` or any other class nothing reads
them, and the generator says so with **PRAG0464** (Warning): use `[ResiliencePolicy]` there.

⚠️ **A name nothing defines is a pipeline that does nothing.** A policy missing from configuration falls
back to `Resilience:Default`, and without a default to a passthrough, with no exception. A typo in the
attribute, or a section that did not make it into production settings, leaves the call unprotected
while everything looks declared; the generated host logs one **Warning** per such name at startup,
naming the policy and the actions that declare it. Read the startup log, and define `Default`.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Showcase example application (code
that compiles and that `Showcase.IntegrationTests` and `Showcase.Tests` exercise) and kept identical to
it by the gate: a `[ResiliencePolicy]` on an action, `[Retry]` and `[CircuitBreaker]` on a message
handler, and the test that proves the breaker opens.

`AddResiliencePolicy` in code, a fallback, and `ExecuteAsResultAsync` outside an operation are used by no
tested application yet, so there is no example of them here: the sections above are the reference.
