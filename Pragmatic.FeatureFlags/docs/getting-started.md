# Getting Started with Pragmatic.FeatureFlags

This guide covers defining feature flags, evaluating them with targeting context, and integrating with the Pragmatic.Design ecosystem.

## Prerequisites

- .NET 10.0+
- `Pragmatic.Abstractions` (provides `IFeatureFlagStore`, `FeatureFlagContext`, `FeatureFlagDefinition`, `FeatureFlagRule`, `FeatureFlagChange`)

## Step 1: Add the Package

```bash
dotnet add package Pragmatic.FeatureFlags
```

## Step 2: Register Services

```csharp
services.AddPragmaticFeatureFlags();
```

This registers `InMemoryFeatureFlagStore` as the default `IFeatureFlagStore` (via `TryAdd`).

When the SG detects `Pragmatic.FeatureFlags` in the project references, it auto-registers this call. No manual registration is needed for basic usage.

## Step 3: Define Flags

For the `InMemoryFeatureFlagStore`, cast and define flags programmatically:

```csharp
var store = serviceProvider.GetRequiredService<IFeatureFlagStore>();
var memoryStore = (InMemoryFeatureFlagStore)store;

memoryStore.Define(new FeatureFlagDefinition
{
    Name = "new-checkout",
    Enabled = false,           // globally disabled
    Description = "New checkout flow with Stripe integration",
    Rules =
    [
        // Enable for enterprise tenants
        new FeatureFlagRule { Type = "tenant", Values = ["acme", "contoso"], Enabled = true },

        // Enable for 20% of users (gradual rollout)
        new FeatureFlagRule { Type = "percentage", Values = ["20"], Enabled = true }
    ]
});
```

A `FeatureFlagDefinition` has:

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` (required) | Flag identifier (case-insensitive lookup) |
| `Enabled` | `bool` | Global state -- used when no rule matches |
| `Description` | `string?` | Human-readable description |
| `Rules` | `IReadOnlyList<FeatureFlagRule>` | Ordered targeting rules |

## Step 4: Evaluate Flags

### Simple Evaluation (No Context)

```csharp
var isEnabled = await store.IsEnabledAsync("new-checkout");
// Returns false for unknown flags (safe default)
```

### Evaluation with Context

```csharp
public class CheckoutService(IFeatureFlagStore flags)
{
    public async Task<bool> UseNewCheckout(string tenantId, string userId)
    {
        var context = new FeatureFlagContext
        {
            TenantId = tenantId,
            UserId = userId
        };

        return await flags.IsEnabledAsync("new-checkout", context);
    }
}
```

### FeatureFlagContext Properties

| Property | Type | Description |
|----------|------|-------------|
| `TenantId` | `string?` | Current tenant identifier |
| `UserId` | `string?` | Current user identifier |
| `Plan` | `string?` | Subscription plan (e.g., `"enterprise"`, `"pro"`, `"free"`) |
| `Environment` | `string?` | Runtime environment (e.g., `"staging"`, `"production"`) |
| `Properties` | `IReadOnlyDictionary<string, string>` | Custom key-value properties for property rules |

Use `FeatureFlagContext.Empty` when no context is available (equivalent to `new FeatureFlagContext()`).

## Step 5: Evaluation Rules

Rules are evaluated **in order** -- first match wins. If no rule matches, the flag's global `Enabled` state is used.

### Rule Types

| Rule Type | Values | Matches When |
|-----------|--------|-------------|
| `tenant` | `["acme", "contoso"]` | `context.TenantId` is in the list (case-insensitive) |
| `user` | `["user-1", "user-2"]` | `context.UserId` is in the list (case-insensitive) |
| `plan` | `["enterprise", "pro"]` | `context.Plan` is in the list (case-insensitive) |
| `environment` | `["staging", "development"]` | `context.Environment` is in the list (case-insensitive) |
| `percentage` | `["20"]` | Deterministic SHA256 hash of `{flagName}:{userId}` falls in bucket |
| `property` | `["region", "eu-west", "eu-east"]` | `context.Properties["region"]` is in `["eu-west", "eu-east"]` |

### Evaluation Flow

```
Evaluate("new-checkout", context)
  |
  v
For each rule (in order):
  |
  +-- Rule matches context? --yes--> return rule.Enabled
  |
  +-- Rule doesn't apply ----------> next rule
  |
  v
No rule matched --> return definition.Enabled (global state)
```

A rule "doesn't apply" when its context value is null (e.g., `tenant` rule but `context.TenantId` is null) or when the value doesn't match the rule's list. In that case, the evaluator moves to the next rule.

### Percentage Rollout

Percentage rules use **deterministic bucketing** via SHA256:

```
hash = SHA256("{flagName}:{userId or tenantId or 'anonymous'}")
bucket = |hash| % 100

if bucket < percentage --> enabled
```

- Same user + same flag = same result (deterministic).
- Different flags = different distribution (flag name is part of the seed).
- 0% = always disabled, 100% = always enabled.
- If neither `UserId` nor `TenantId` is set, `"anonymous"` is used as the seed.

## Step 6: Context Provider (Optional)

For automatic context resolution from ambient state (HTTP request, tenant, user), implement `IFeatureFlagContextProvider`:

```csharp
public class HttpFeatureFlagContextProvider(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IHostEnvironment hostEnv) : IFeatureFlagContextProvider
{
    public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new FeatureFlagContext
        {
            TenantId = tenantContext.TenantId,
            UserId = currentUser.UserId,
            Plan = tenantContext.Plan,
            Environment = hostEnv.EnvironmentName
        });
    }
}
```

**Note**: the bridging between `ITenantContext` and `FeatureFlagContext.TenantId` is manual. You must explicitly copy the values in your context provider. There is no automatic wiring between the two.

Register it like any other service:

```csharp
services.AddScoped<IFeatureFlagContextProvider, HttpFeatureFlagContextProvider>();
```

Then inject `IFeatureFlags` instead of `IFeatureFlagStore` and the context is resolved for you:

```csharp
public class CheckoutService(IFeatureFlags flags)
{
    public async Task<bool> UseNewCheckout(CancellationToken ct = default)
        => await flags.IsEnabledAsync<NewCheckout>(ct);   // no context plumbing
}
```

`IFeatureFlags` is registered by `AddPragmaticFeatureFlags()` (scoped, since a provider usually reads
per-request state). Without a provider it evaluates against `FeatureFlagContext.Empty`, so the code above
keeps working — global state and percentage rules apply, targeting rules simply do not match.

Keep using `IFeatureFlagStore` directly when the context is deliberately **not** the ambient one: a
background job evaluating on behalf of another tenant, or previewing what a different user would see.

## Rule Composition Examples

```csharp
// Beta: enterprise tenants + 10% of all other users
Rules =
[
    new FeatureFlagRule { Type = "plan", Values = ["enterprise"], Enabled = true },
    new FeatureFlagRule { Type = "percentage", Values = ["10"], Enabled = true }
]

// Disabled for a specific tenant, enabled for everyone else
Rules =
[
    new FeatureFlagRule { Type = "tenant", Values = ["legacy-corp"], Enabled = false },
]
// Global Enabled = true --> everyone except "legacy-corp" gets the feature

// Environment gate + user allowlist
Rules =
[
    new FeatureFlagRule { Type = "environment", Values = ["production"], Enabled = false },
    new FeatureFlagRule { Type = "user", Values = ["admin-1"], Enabled = true },
]
// In production: disabled. Outside production: only admin-1 gets it.
```

## Watch for Changes

React to flag changes in real-time:

```csharp
await foreach (var change in store.WatchAsync(cancellationToken))
{
    logger.LogInformation(
        "Flag '{Flag}' changed: {Was} -> {Is} at {Time}",
        change.FlagName,
        change.WasEnabled,
        change.IsEnabled,
        change.Timestamp);
}
```

The `InMemoryFeatureFlagStore` emits a `FeatureFlagChange` when `Define()` changes a flag's `Enabled` state. Change notifications use `Channel<T>` internally.

## Next Steps

- [Stores](stores.md) -- IFeatureFlagStore interface, in-memory store, custom backends
