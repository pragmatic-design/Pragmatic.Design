---
title: "Pragmatic.FeatureFlags"
description: "Context-aware feature flag evaluation for .NET 10: targeting rules, percentage rollout, and"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.FeatureFlags/README.md
sidebar:
  order: 0
  label: Overview
---
Context-aware feature flag evaluation for .NET 10: targeting rules, percentage rollout, and
deterministic bucketing.

## The Problem

Apps need runtime control over feature visibility: gradual rollouts, A/B tests, beta programs, kill
switches. Plain configuration gives you on/off, but the same value applies to every user, tenant, and
environment, so teams hand-roll targeting:

```csharp
// Without Pragmatic: hand-rolled targeting, duplicated and inconsistent
var isEnabled = betaTenants?.Contains(tenantId) == true
    || ComputeHash(userId) % 100 < rolloutPercent;   // different result per hash/seed/rounding
```

That duplicates the hash-and-check pattern, gives no consistency guarantee (the same user flips between
requests), has no change detection (caches serve stale toggles until restart), and conflates static
configuration with context-dependent evaluation.

## The Solution

Define flags with targeting rules; a deterministic evaluation engine handles the rest: stable
per-user/tenant bucketing (the same input always resolves the same way), percentage rollout, and change
watching.

```csharp
// Define() is on the in-memory store registered by AddPragmaticFeatureFlags()
var store = serviceProvider.GetRequiredService<InMemoryFeatureFlagStore>();
store.Define(new FeatureFlagDefinition
{
    Name = "new-checkout",
    Enabled = false,
    Rules = [ FeatureFlagRule.Percentage(20), FeatureFlagRule.Tenant("beta-co") ]   // 20% rollout + beta tenant
});

// Evaluate against an explicit context…
if (await store.IsEnabledAsync("new-checkout", context, ct)) { /* new path */ }

// …or let IFeatureFlags resolve the ambient one through your IFeatureFlagContextProvider:
if (await flags.IsEnabledAsync<NewCheckout>(ct)) { /* new path */ }
```

`IFeatureFlags` knows the caller through an `IFeatureFlagContextProvider`. `AddPragmaticFeatureFlags`
registers `TenantFeatureFlagContextProvider`, which fills the request's tenant, so a tenant targeting rule
works out of the box in a multi-tenant application. Register your own to add the user, the plan or the
environment; it replaces the default. Without a tenant the context is `FeatureFlagContext.Empty`: the
global state and percentage rules apply, targeting rules never match.

An operation that exists only where a flag is on says so on its class:

```csharp
[Query<Reservation, ReservationSummaryDto>]
[RequiresFeature<EarlyCheckInFlag>]     // or [RequiresFeature("early-check-in")]
[Endpoint(HttpVerb.Get, "api/reservations/early-check-in")]
public partial class EarlyCheckInReservationsQuery { … }
```

The generated invoker of a query, an action or a mutation evaluates the flag after validation and
authorization and answers **404** when it is off: for that caller the operation does not exist. Several
attributes must all be on. It fails closed: a host with no `IFeatureFlags` refuses it, and is warned with
`PRAG1616`.

Strongly-typed flags (`IFeatureFlag`), a configuration-backed store (`Pragmatic.FeatureFlags.Configuration`)
and change notifications are all supported. Changes are broadcast: every watcher sees every change.

## Installation

```bash
dotnet add package Pragmatic.FeatureFlags
dotnet add package Pragmatic.FeatureFlags.Configuration   # optional: flags read from IConfiguration
```

## Status

**Functional** within 1.0.0-alpha: the evaluation engine, targeting rules, deterministic bucketing,
strongly-typed flags, and the stores. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/feature-flags/concepts/) | Evaluation engine, rule types, deterministic bucketing, vs configuration |
| [Getting Started](/modules/feature-flags/getting-started/) | Define a flag, add targeting, evaluate it |
| [Stores](/modules/feature-flags/stores/) | Pluggable backends, change watching, strongly-typed flags |
| [Common Mistakes](/modules/feature-flags/common-mistakes/) | The most frequent flag pitfalls |
| [Troubleshooting](/modules/feature-flags/troubleshooting/) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/feature-flags/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.FeatureFlags is **MIT-licensed**.
