# Pragmatic.FeatureFlags

Context-aware feature flag evaluation for .NET 10 — targeting rules, percentage rollout, and
deterministic bucketing.

## The Problem

Apps need runtime control over feature visibility — gradual rollouts, A/B tests, beta programs, kill
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

Define flags with targeting rules; a deterministic evaluation engine handles the rest — stable
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

`IFeatureFlags` knows the caller only through an `IFeatureFlagContextProvider` you register — no package
ships one, and multi-tenancy is not bridged automatically. Without it every evaluation uses
`FeatureFlagContext.Empty`: the global state and percentage rules apply, targeting rules never match.

Strongly-typed flags (`IFeatureFlag`), a configuration-backed store (`Pragmatic.FeatureFlags.Configuration`)
and change notifications are all supported. Changes are broadcast: every watcher sees every change.

## Installation

```bash
dotnet add package Pragmatic.FeatureFlags
dotnet add package Pragmatic.FeatureFlags.Configuration   # optional: flags read from IConfiguration
```

## Status

The evaluation engine, targeting rules, deterministic bucketing, strongly-typed flags, and stores are
functional within 1.0.0-alpha. See the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | Evaluation engine, rule types, deterministic bucketing, vs configuration |
| [Getting Started](docs/getting-started.md) | Define a flag, add targeting, evaluate it |
| [Stores](docs/stores.md) | Pluggable backends, change watching, strongly-typed flags |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent flag pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.FeatureFlags is **MIT-licensed**.
