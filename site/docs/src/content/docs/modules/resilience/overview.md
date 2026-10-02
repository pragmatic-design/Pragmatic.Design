---
title: "Pragmatic.Resilience"
description: "Native, AOT-safe resilience for .NET 10 — strategy composition, a fluent builder, DI integration, and"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Resilience/README.md
sidebar:
  order: 0
  label: Overview
---
Native, AOT-safe resilience for .NET 10 — strategy composition, a fluent builder, DI integration, and
source-generator support. No Polly dependency.

## The Problem

Distributed systems fail: calls time out, databases go down, APIs error. The usual fix — Polly policies
or hand-rolled retry loops — scatters resilience across the codebase, pulls in external dependencies,
and treats every failure the same, whether it's transient (a network blip) or permanent (a validation
error).

```csharp
// Without Pragmatic: manual composition, repeated at every call site
var combined = Policy.WrapAsync(
    Policy.TimeoutAsync(10),
    Policy.Handle<HttpRequestException>().CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)),
    Policy.Handle<HttpRequestException>().WaitAndRetryAsync(3, a => TimeSpan.FromMilliseconds(200 * Math.Pow(2, a))));
await combined.ExecuteAsync(ct => http.PostAsJsonAsync("/charges", request, ct), ct);
```

## The Solution

Declare a **policy name**; the generator composes the pipeline. One attribute, several strategies, zero
manual wiring — and it distinguishes transient exceptions (retry/break) from `Result` failures
(validation, not-found), which pass through unchanged.

```csharp
[DomainAction]
[ResiliencePolicy("payment-gateway")]
public partial class ChargeCustomerAction : DomainAction<PaymentResult>
{
    public override async Task<Result<PaymentResult, IError>> Execute(CancellationToken ct)
    {
        // wrapped by the "payment-gateway" pipeline automatically:
        // retry + circuit breaker + timeout on exceptions; Result failures pass through
        var response = await _http.PostAsJsonAsync("/charges", _request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentResult>(ct)!;
    }
}
```

⚠️ The name must match a configured policy. A name nobody configured, with no `Resilience:Default`,
runs the action with no resilience at all; the generated host logs one warning per such name at
startup, naming the policy and the operations that declare it. On a `[Mutation]` the pipeline wraps the
whole invocation, save included, and each retry starts from a clean unit of work.

## Installation

```bash
dotnet add package Pragmatic.Resilience
```

Strategies — retry (with backoff + jitter), circuit breaker, timeout, hedging, fallback, rate limiter,
bulkhead — compose in a defined pipeline order; configure named policies in `Program.cs` or via the
fluent builder. See [Policies](/modules/resilience/policies/).

## The declarations other modules read

`Pragmatic.Resilience.Attributes` also holds `[Retry]`, `[Timeout]`, `[CircuitBreaker]` and
`BackoffStrategy` — declared here and read elsewhere. `Pragmatic.Jobs` and `Pragmatic.Messaging`
each own an engine: a job retry is a lease and a durable reschedule, a message retry is a
redelivery. Those stay apart. ⚠️ Only those two read them — and `[CircuitBreaker]` only the messaging
engine: anywhere else the attribute does nothing, and the generator reports **PRAG0464**. On a domain
action use `[ResiliencePolicy]`. «How many attempts, what backoff, what base delay» is one question,
so it has one declaration.

```csharp
[Retry(MaxAttempts = 3, Strategy = BackoffStrategy.ExponentialWithJitter, BaseDelayMs = 500)]
```

The attribute carries no numbers of its own. Leave a property out and the engine that reads the
declaration applies its own — a job waits far longer between attempts than a redelivery does, and
both are deliberate. ⚠️ Leaving a property out is not the same as setting it to `0`: the first is
absent from the metadata a reader sees, the second is a value you chose, and an engine may reject it
(the job engine reports `PRAG2504` for `MaxAttempts = 0`).

One declaration, one curve. `Exponential` waits the base delay before the first retry and doubles
from there — `base × 2^(n-1)` — and both engines stop growing at thirty minutes rather than asking
for a wait measured in days. Pinned by `TheTwoRetryEnginesAgreeTests`, which executes the job side
and reads the message side out of the generated pipeline.

## Status

The strategy set, pipeline composition, DI integration, and the `[ResiliencePolicy]` generator are
functional within 1.0.0-alpha. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/resilience/concepts/) | Strategy model, pipeline order, transient vs permanent failures |
| [Getting Started](/modules/resilience/getting-started/) | Declare a policy, wrap an action/call |
| [Policies](/modules/resilience/policies/) | Every strategy, parameters, configuration, named policies |
| [Common Mistakes](/modules/resilience/common-mistakes/) | The most frequent resilience pitfalls |
| [Troubleshooting](/modules/resilience/troubleshooting/) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/resilience/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Resilience is **MIT-licensed**.
