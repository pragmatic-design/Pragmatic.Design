---
title: "Feature flags: is this on, for this caller"
description: "> **Scope**: `src/Pragmatic.Abstractions/FeatureFlags/`, 9 files."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/feature-flags.md
sidebar:
  order: 13
---
> **Scope**: `src/Pragmatic.Abstractions/FeatureFlags/`, 9 files.
> `IFeatureFlag` · `IFeatureFlags` · `IFeatureFlagStore` · `IFeatureFlagContextProvider` ·
> `FeatureFlagContext` · `FeatureFlagDefinition` · `FeatureFlagRule` · `FeatureFlagChange` ·
> `FeatureFlagStoreExtensions`
>
> **Not covered here**: the evaluation engine, the in-memory store and the DI extension live in the
> `Pragmatic.FeatureFlags` package, and the configuration-backed store in
> `Pragmatic.FeatureFlags.Configuration`; only their contracts are here.
> `ICurrentUser` and `ITenantContext`, the usual inputs to a context provider, are
> [identity](/modules/abstractions/how-it-works-identity/) and multi-tenancy.

For the member-by-member catalogue, see [interfaces](/modules/abstractions/interfaces/#11-feature-flags). This
document is about how the pieces fit together.

## A flag is a type, not a string

`IFeatureFlag` has two `static abstract` members: `Name`, the store key, and `Description`, used
for seeding and diagnostics. A flag is declared as a type:

```csharp
public sealed class EarlyCheckInFlag : IFeatureFlag
{
    public static string Name => "early-check-in";
    public static string? Description => "Allow early check-in for premium tenants";
}
```

and asked for as a type: `await flags.IsEnabledAsync<EarlyCheckInFlag>(ct)`.

The reason is greppability and compile-time safety. A string key is invisible to rename, invisible
to find-usages, and a typo becomes a silently disabled feature, the worst failure mode, because
everything still runs. A type gives you a declaration site to hang the description on, a compiler
error when it disappears, and one place to find every consumer. The same shape shows up again for
culture scopes in `Pragmatic.Internationalization`, which names this one as its reference.

The store still speaks strings, because a store talks to a database or a remote service. The type
is the application-facing surface; `Name` is the wire format.

## The four contracts, and who calls which

| Contract | Called by | Answers |
|---|---|---|
| `IFeatureFlags` | application code | "is this flag on, right now, for whoever is asking" |
| `IFeatureFlagContextProvider` | the runtime, once per evaluation | "who is asking" |
| `IFeatureFlagStore` | the runtime | "what does this flag say, given that context" |
| `IFeatureFlag` | nobody: it *is* the key | n/a |

`IFeatureFlags` is the one your actions and mutations use. It takes no context: the runtime builds
it. That is the whole point of the split: a booking action should not have to assemble a tenant id,
a user id, a plan and an environment just to ask a yes/no question.

`ContextualFeatureFlags` in `Pragmatic.FeatureFlags` is the implementation that closes the loop: it
resolves the context provider **optionally** (`GetService`, not `GetRequiredService`) and falls back
to `FeatureFlagContext.Empty` when none is registered. An app with no provider still evaluates
flags: global on/off works, and percentage rules still bucket, because the evaluator seeds them
with `"anonymous"` when there is no user or tenant. Nothing silently returns false for want of
wiring.

`IFeatureFlags` also exposes `GetContextAsync()`, the context that *would* be used right now. It is
there for the two cases where the ambient shortcut is not enough: evaluating several flags against
one snapshot, and logging what a decision was based on.

To make evaluation caller-aware, that is the extension point: implement
`IFeatureFlagContextProvider` and register it. The Showcase provider is the reference shape: it
injects `ITenantContext`, `ICurrentUser` and `IHostEnvironment` and fills the matching fields of
`FeatureFlagContext`.

## Rules, and why the edges are hard

A `FeatureFlagDefinition` carries a global `Enabled` and an ordered list of `FeatureFlagRule`. The
evaluator walks the rules in order and **the first rule that matches wins**; a rule that does not
match returns nothing and evaluation moves on. Rules are built through typed factories
(`FeatureFlagRule.Percentage(...)`, `.Tenant(...)`, `.User(...)`, `.Plan(...)`, `.Property(...)`),
and `Denying()` flips a rule so that matching means `false`.

Six rule types are understood: `tenant`, `user`, `plan`, `environment`, `percentage`, `property`,
matched case-insensitively. `FeatureFlagRule.KnownTypes` and `IsKnownType` expose that set. A rule
with an unrecognised type is skipped, not an error: a store can carry rules written by a newer
version of the app without breaking the older one.

Percentage rollout is where the edge cases are deliberate:

| Percentage | Result |
|---|---|
| `<= 0` | `false` for everyone; overrides the global `Enabled` and stops evaluation |
| `>= 100` | `true` for everyone; same, in the other direction |
| in between | in-bucket → the rule's `Enabled`; out of bucket → no match, try the next rule |

The two bounds are absolute on purpose. `Percentage(0)` is a kill switch that beats every later rule
and the global flag; `Percentage(100)` is the opposite. If a bound merely "did not match", a stale
rule further down the list could resurrect a feature someone had just turned off.

Bucketing is a SHA-256 over `"{flagName}:{seed}"` where the seed is `UserId ?? TenantId ??
"anonymous"`. Two properties fall out of that, and both matter:

- **stable**: the same user gets the same answer on every request, so a half-rolled-out feature
  does not flicker mid-session;
- **per-flag**: the flag name is in the hash, so the same 10% of users are not the guinea pigs for
  every rollout.

## Watching for changes

`IFeatureFlagStore.WatchAsync` streams `FeatureFlagChange`. That is how a running host notices a
flag was flipped without polling on every evaluation, and it is why the store contract owns
evaluation rather than being a key-value read: `IsEnabledAsync(flagName, context, ct)` is on the
store, so an implementation backed by a remote service can evaluate remotely instead of shipping
every definition to every process.

`FeatureFlagStoreExtensions` gives the same typed form to code that holds a store directly rather
than `IFeatureFlags`: `IsEnabledAsync<TFlag>(ct)`, `IsEnabledAsync<TFlag>(context, ct)` and
`GetDefinitionAsync<TFlag>(ct)`, written as C# 14 extension members over `IFeatureFlagStore`.

## What the generator does

Nothing type-specific: there are no attributes in this feature, so there is nothing per-flag to
generate. The hook is presence-of-module, and the detection probe is worth knowing exactly:

```csharp
HasFeatureFlags = TypeExists(compilation, "Pragmatic.FeatureFlags.FeatureFlagServiceCollectionExtensions");
```

It probes the **runtime package's DI class**, not a type in `Pragmatic.Abstractions`. Referencing
only `Pragmatic.Abstractions` gives you the contracts but does not turn the feature on; with the
runtime package referenced, the generated host emits `services.AddPragmaticFeatureFlags()` into its
infrastructure block, and that registration wires the store, the evaluator and the optional context
provider for you. `AddPragmaticFeatureFlags` registers the concrete store and the interface on the
*same instance*, so a seeder can resolve `InMemoryFeatureFlagStore` directly without downcasting
from `IFeatureFlagStore`.

One naming detail with a visible effect: `FeatureFlags` is one of the nineteen folder names on the
source generator's list of segments excluded from sub-boundary inference. Putting flag
declarations in `Booking/Infrastructure/FeatureFlags/` therefore does not produce an
`IBookingFeatureFlagsActions` interface: the folder is infrastructure, not a slice of the domain.

## External references

Named here, described where they live:

- **`Pragmatic.FeatureFlags`**: `FeatureFlagEvaluator` (the rule engine),
  `InMemoryFeatureFlagStore`, `FeatureFlagChangeBroadcaster`, `ContextualFeatureFlags` and
  `AddPragmaticFeatureFlags()`.
- **`Pragmatic.FeatureFlags.Configuration`**: `ConfigurationFeatureFlagStore`, the store backed by
  `appsettings.json`. A separate package: referencing `Pragmatic.FeatureFlags` alone does not bring
  it in.
- **`Pragmatic.Agent.Client`**: `AgentFeatureFlagStore`, a store that serves definitions pushed by
  a connected Agent and falls back to the local store when it is not. Which rules mean what is
  decided by the registered store, so it is worth knowing which one is registered.
- **`Pragmatic.Abstractions/Configuration`**: `IConfigurationStore` is the sibling contract for
  settings. Feature flags are separate because a flag is evaluated against a caller; a setting is
  read.
- **`Pragmatic.Abstractions/Identity`**: `ICurrentUser`, and multi-tenancy's `ITenantContext`, are
  what a context provider typically reads to fill `FeatureFlagContext`.
