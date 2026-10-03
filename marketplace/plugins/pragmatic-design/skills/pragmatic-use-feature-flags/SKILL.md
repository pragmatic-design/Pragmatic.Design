---
name: pragmatic-use-feature-flags
description: Use when the app needs runtime feature toggles — gradual rollout, A/B tests, beta programs, kill switches, per-tenant or per-user targeting — with Pragmatic.FeatureFlags.
---

# Pragmatic Use Feature Flags

**Covers:** Feature flags with Pragmatic.FeatureFlags from NuGet — context-aware evaluation, targeting rules, percentage rollout with deterministic per-user/tenant bucketing, and change watching.

A deterministic evaluation engine: define flags with targeting rules, evaluate them against a context.
The same user/tenant always resolves the same way (stable bucketing) — no flapping between requests.

## When to use

- Percentage rollout, beta cohorts, kill switches, A/B tests.
- Toggles that depend on user/tenant/environment, not a single global on/off.

## Packages

```xml
<PackageReference Include="Pragmatic.FeatureFlags" Version="1.0.0-alpha.1" />
```

## Core pattern

Rules are built with the static factories on `FeatureFlagRule` — there is no `PercentageRule` /
`TenantRule` type. They are evaluated **in order, first match wins**; if none matches, the flag's global
`Enabled` applies.

```csharp
using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Providers;

// register the in-memory store (see "Registration" below for what the host already does)
services.AddPragmaticFeatureFlags();

// define — Define() lives on the concrete InMemoryFeatureFlagStore, not on IFeatureFlagStore;
// AddPragmaticFeatureFlags registers both against the same instance, so resolve the concrete type to seed.
var store = provider.GetRequiredService<InMemoryFeatureFlagStore>();
store.Define(new FeatureFlagDefinition
{
    Name = "new-checkout",
    Enabled = false,
    Rules =
    [
        FeatureFlagRule.Tenant("blocked-co").Denying(),   // explicit opt-out, listed first so it wins
        FeatureFlagRule.Tenant("beta-co"),                // always on for this tenant
        FeatureFlagRule.Percentage(20)                    // otherwise 20% of users/tenants
    ]
});

// evaluate against an explicit context
var context = new FeatureFlagContext { TenantId = tenantId, UserId = userId };
if (await store.IsEnabledAsync("new-checkout", context, ct))
{
    // new path
}
```

In application code prefer `IFeatureFlags`, which resolves the context for you from the registered
`IFeatureFlagContextProvider` (tenant, user, plan, environment) — inject it and ask directly:

```csharp
public sealed class CheckoutService(IFeatureFlags flags)
{
    public async Task<Result> Checkout(CancellationToken ct)
    {
        if (await flags.IsEnabledAsync("new-checkout", ct)) { /* new path */ }
        // …
    }
}
```

Register the provider yourself; the values are **not** bridged automatically from multi-tenancy:

```csharp
services.AddScoped<IFeatureFlagContextProvider, FlagContextFromCurrentUser>();

public sealed class FlagContextFromCurrentUser(ICurrentUser user, IHostEnvironment env) : IFeatureFlagContextProvider
{
    public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default) =>
        Task.FromResult(new FeatureFlagContext
        {
            UserId = user.IsAuthenticated ? user.Id : null,
            TenantId = user.TenantId,
            Environment = env.EnvironmentName,
            // Plan, Properties: from wherever the application keeps them
        });
}
```

Without a provider, evaluation falls back to `FeatureFlagContext.Empty` — global state and percentage
rules still apply, targeting rules never match. Reach for `IFeatureFlagStore` directly only when the
context is *not* the ambient one (a background job acting for another tenant).

**Registration.** The Pragmatic host calls `AddPragmaticFeatureFlags()` when an assembly declares an
`IFeatureFlag` (next section). With string names only, nothing declares one: call it yourself in an
`IStartupStep`, or `IFeatureFlags` does not resolve. For another store, `AddPragmaticFeatureFlags<TStore>()`
**replaces** the in-memory one; `ConfigurationFeatureFlagStore` (package `Pragmatic.FeatureFlags.Configuration`)
reads the flags from configuration.

Available factories: `Percentage(int)`, `Tenant(params string[])`, `User(params string[])`,
`Plan(params string[])`, `Property(key, params string[])`. Add `.Denying()` to turn a match into a
denial. Rule types and values match case-insensitively; an unrecognized rule type is silently skipped,
which is why the factories are preferable to setting `Type` by hand. The known types are
`FeatureFlagRule.KnownTypes` — `tenant`, `user`, `plan`, `environment`, `percentage`, `property`
(`environment` has no factory: `new FeatureFlagRule { Type = "environment", Values = ["Staging"] }`).

### Percentage rollout — what "20%" means

- The bucket is a stable hash of **flag name + `UserId`**, falling back to `TenantId`, falling back to
  the literal `"anonymous"`. ⚠️ Every anonymous caller shares one bucket: for signed-out traffic a
  percentage is all-or-nothing, decided by the flag's name. Roll out to anonymous users with a
  `Property` rule over something you put in the context (a cookie id), not with a percentage.
- A user **outside** the bucket does not match, and evaluation moves to the next rule.
- ⚠️ The borders short-circuit: `Percentage(0)` answers `false` and `Percentage(100)` answers `true`
  for everyone, ignoring the rules after them and the rule's own `enabled`. That makes `Percentage(0)`
  a kill switch — so it must be **last** unless killing everything is what you mean.
- The flag name is part of the hash: two flags at 20% do not select the same 20%.

## Flags from configuration

`Pragmatic.FeatureFlags.Configuration` — `services.AddConfigurationFeatureFlagStore()` replaces the
in-memory store and reads the `FeatureFlags` section (another name as its argument):

```json
{
  "FeatureFlags": {
    "dark-mode": true,
    "new-checkout": {
      "Enabled": false,
      "Description": "Redesigned checkout",
      "Rules": [
        { "Type": "tenant", "Values": [ "blocked-co" ], "Enabled": false },
        { "Type": "tenant", "Values": [ "beta-co" ] },
        { "Type": "percentage", "Values": [ "20" ] }
      ]
    }
  }
}
```

A rule's `"Enabled": false` is `.Denying()`. ⚠️ Unlike `InMemoryFeatureFlagStore.Define`, which throws
on an unknown rule type, this store does not validate: a misspelt `"Type"` is a rule that never
matches. Check `FeatureFlagRule.IsKnownType` in a start-up test over the section.

Across instances, at runtime: the Agent (`UseAgent()`) replaces the store with its shared `flags/…` keys,
keeping the previous store as the fallback when the Agent is unreachable — `pragmatic-use-distributed`.

## Strongly-typed flags

Avoids magic strings — an unknown flag name evaluates to `false`, so a typo disables the feature silently.

```csharp
public sealed class NewCheckout : IFeatureFlag
{
    public static string Name => "new-checkout";
    public static string? Description => "Redesigned checkout — gradual rollout";
}

if (await store.IsEnabledAsync<NewCheckout>(context, ct)) { /* … */ }
```

## Watching for changes

`WatchAsync` broadcasts: every concurrent watcher observes every change. Treat an event as
"this flag moved, re-read it" — `FeatureFlagChange` carries only `WasEnabled`/`IsEnabled`, so a change
caused by a rule edit reports the same value in both.

```csharp
await foreach (var change in store.WatchAsync(ct))
    logger.LogInformation("Flag {Flag} changed", change.FlagName);
```

`InMemoryFeatureFlagStore` emits on any difference in the definition (rules and rollout included);
`ConfigurationFeatureFlagStore` emits only on `Enabled` transitions. See
`Pragmatic.FeatureFlags/docs/` for the rule catalogue and store options.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Warehouse and Showcase example
applications — code that compiles and that `Showcase.IntegrationTests` and `Warehouse.IntegrationTests`
exercise — and kept identical to it by the gate: a typed flag read at each request, the in-memory
definitions with the rule factories, the context provider, the ambient and the explicit evaluation, and
flags from configuration.

`WatchAsync` is used by no tested application yet, so there is no example of it here: the sections above
are the reference.
