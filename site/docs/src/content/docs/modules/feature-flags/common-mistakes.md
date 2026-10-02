---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.FeatureFlags. Each section shows the wrong approach, the correct approach, and explai"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.FeatureFlags/docs/common-mistakes.md
sidebar:
  order: 4
---
These are the most common issues developers encounter when using Pragmatic.FeatureFlags. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Using Configuration Instead of Feature Flags

**Wrong:**

```csharp
// Using IConfiguration for features that need targeting
public class CheckoutService(IConfiguration config)
{
    public async Task ProcessAsync()
    {
        var useNew = config.GetValue<bool>("Features:NewCheckout");
        // Same for all users, all tenants, all environments
    }
}
```

**Right:**

```csharp
public class CheckoutService(IFeatureFlagStore flags, ITenantContext tenant)
{
    public async Task ProcessAsync()
    {
        var context = new FeatureFlagContext { TenantId = tenant.TenantId };
        var useNew = await flags.IsEnabledAsync("new-checkout", context);
        // Can target specific tenants, users, plans, percentages
    }
}
```

**Why:** `IConfiguration` (and `IConfigurationStore`) provides flat key-value lookup -- the same value for every request. Feature flags need context-based evaluation: the same flag can be enabled for tenant A and disabled for tenant B. Use Configuration for settings (connection strings, limits). Use FeatureFlags for features that need targeting or gradual rollout.

---

## 2. Not Providing Context When Evaluating

**Wrong:**

```csharp
public class CheckoutService(IFeatureFlagStore flags)
{
    public async Task ProcessAsync()
    {
        // No context -- tenant/user/plan rules can never match
        var isEnabled = await flags.IsEnabledAsync("new-checkout");
    }
}
```

**Right:**

```csharp
public class CheckoutService(IFeatureFlagStore flags, ITenantContext tenant, ICurrentUser user)
{
    public async Task ProcessAsync()
    {
        var context = new FeatureFlagContext
        {
            TenantId = tenant.TenantId,
            UserId = user.UserId,
            Plan = "enterprise"  // from subscription service
        };
        var isEnabled = await flags.IsEnabledAsync("new-checkout", context);
    }
}
```

**Why:** Without context, only the global `Enabled` state and percentage rules with `"anonymous"` seed are evaluated. Tenant, user, plan, environment, and property rules require their respective context values to match. Always provide the richest context available.

---

## 3. Using Magic Strings for Flag Names

**Wrong:**

```csharp
// Flag name repeated as strings throughout the codebase
await flags.IsEnabledAsync("new-checkout", context);
// ... in another file:
await flags.IsEnabledAsync("new_checkout", context);  // typo: underscore instead of hyphen
// ... in yet another file:
await flags.IsEnabledAsync("newCheckout", context);    // typo: different casing
```

**Right:**

```csharp
// Define the flag as a type
public sealed class NewCheckout : IFeatureFlag
{
    public static string Name => "new-checkout";
    public static string? Description => "New checkout flow with Stripe integration";
}

// Usage: compile-time safety
await flags.IsEnabledAsync<NewCheckout>(context);
```

**Why:** String-based flag names are prone to typos. Since unknown flags return `false` (safe default), a misspelled flag name silently evaluates to `false` -- the feature appears permanently disabled with no error. `IFeatureFlag` types provide compile-time safety and IDE navigation. Note: flag name lookup in the store is case-insensitive, but that only helps with casing differences, not typos.

---

## 4. Wrong Rule Order (First Match Wins)

**Wrong:**

```csharp
// Goal: enable for enterprise, BUT disable for "legacy-corp" (enterprise tenant)
Rules =
[
    new FeatureFlagRule { Type = "plan", Values = ["enterprise"], Enabled = true },
    new FeatureFlagRule { Type = "tenant", Values = ["legacy-corp"], Enabled = false },
]
// Result: legacy-corp gets Enabled=true because the plan rule matches first!
```

**Right:**

```csharp
// Deny rules BEFORE allow rules
Rules =
[
    new FeatureFlagRule { Type = "tenant", Values = ["legacy-corp"], Enabled = false },
    new FeatureFlagRule { Type = "plan", Values = ["enterprise"], Enabled = true },
]
// Result: legacy-corp hits the tenant deny rule first --> disabled
// Other enterprise tenants hit the plan rule --> enabled
```

**Why:** Rules are evaluated in order and first match wins. If a broad allow rule comes before a specific deny rule, the deny never fires. Always put specific deny rules first, then specific allow rules, then broad percentage rules.

**Ordering guideline:**

1. Specific deny rules (block specific tenants/users)
2. Specific allow rules (enable for specific tenants/users/plans)
3. Broad percentage rules (gradual rollout)

---

## 5. Assuming Automatic Bridging Between ITenantContext and FeatureFlagContext

**Wrong:**

```csharp
public class CheckoutService(IFeatureFlagStore flags)
{
    public async Task ProcessAsync()
    {
        // Assuming TenantId is automatically populated from ITenantContext
        var context = new FeatureFlagContext();
        await flags.IsEnabledAsync("new-checkout", context);
        // context.TenantId is null -- tenant rules never match
    }
}
```

**Right:**

```csharp
public class CheckoutService(IFeatureFlagStore flags, ITenantContext tenant)
{
    public async Task ProcessAsync()
    {
        // Explicitly bridge the context
        var context = new FeatureFlagContext
        {
            TenantId = tenant.TenantId
        };
        await flags.IsEnabledAsync("new-checkout", context);
    }
}
```

**Or with a context provider:**

```csharp
public class HttpFeatureFlagContextProvider(
    ITenantContext tenant, ICurrentUser user, IHostEnvironment env) : IFeatureFlagContextProvider
{
    public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
        => Task.FromResult(new FeatureFlagContext
        {
            TenantId = tenant.TenantId,
            UserId = user.UserId,
            Environment = env.EnvironmentName
        });
}
```

**Why:** `FeatureFlagContext` and `ITenantContext` are independent systems. There is no automatic wiring. You must explicitly copy `ITenantContext.TenantId` into `FeatureFlagContext.TenantId`. The `IFeatureFlagContextProvider` pattern centralizes this bridging so you don't repeat it in every flag check.

---

## 6. Misunderstanding Percentage Rollout Determinism

**Wrong understanding:**

```csharp
// "I set 20% rollout but sometimes the same user gets different results"
var result1 = await flags.IsEnabledAsync("feature-a", new FeatureFlagContext { UserId = "user-1" });
var result2 = await flags.IsEnabledAsync("feature-a", new FeatureFlagContext { UserId = "user-1" });
// result1 == result2 (always) -- if they differ, something is wrong with context
```

**Wrong assumption:**

```csharp
// "20% rollout on flag-a means the same 20% on flag-b"
// WRONG -- each flag has an independent distribution because the flag name is part of the hash seed
```

**Right understanding:**

```
hash = SHA256("{flagName}:{userId}")
bucket = |hash| % 100

// Same user + same flag = SAME bucket (deterministic)
// Same user + different flag = DIFFERENT bucket (independent)
// Going from 20% to 30% = ADDS users, never removes existing ones
```

**Why:** The hash seed includes the flag name. This means a user in the 20% bucket for `flag-a` might not be in the 20% bucket for `flag-b`. This is intentional -- it prevents correlation between rollouts. It also means increasing the percentage is additive: users in the 10% cohort are always in the 20% cohort.

---

## 7. Not Handling Unknown Flags Explicitly

**Wrong:**

```csharp
// Relying on unknown flags returning false without validation
var isEnabled = await flags.IsEnabledAsync("new-chekout", context);  // typo!
// Returns false silently -- feature appears permanently disabled
```

**Right:**

```csharp
// Use strongly-typed flags for compile-time safety
public sealed class NewCheckout : IFeatureFlag
{
    public static string Name => "new-checkout";
    public static string? Description => "New checkout flow";
}

var isEnabled = await flags.IsEnabledAsync<NewCheckout>(context);
// Typos are caught at compile time

// Or if using strings, verify the flag exists during startup
var definition = await flags.GetDefinitionAsync("new-checkout");
if (definition is null)
    logger.LogWarning("Feature flag 'new-checkout' not defined");
```

**Why:** Unknown flags return `false` by design (safe default). This means a typo in a flag name does not throw -- it silently disables the feature. Use `IFeatureFlag` types for compile-time safety, or validate flag existence during application startup.

---

## 8. Casting IFeatureFlagStore to InMemoryFeatureFlagStore in Production

**Wrong:**

```csharp
// In a startup step or service
var store = (InMemoryFeatureFlagStore)serviceProvider.GetRequiredService<IFeatureFlagStore>();
store.Define(new FeatureFlagDefinition { ... });
// This cast fails when you swap to a database store
```

**Right:**

```csharp
// Define flags through the store abstraction or a seeding mechanism
public class FeatureFlagSeeder(IFeatureFlagStore store)
{
    public async Task SeedAsync()
    {
        // Check if the store supports programmatic definition
        if (store is InMemoryFeatureFlagStore memoryStore)
        {
            memoryStore.Define(new FeatureFlagDefinition { ... });
        }
        // Database stores have their own seeding mechanism
    }
}
```

**Why:** `InMemoryFeatureFlagStore` is the default for development and testing. In production, you will likely use a database store, Azure App Configuration, or LaunchDarkly. Casting to the concrete type breaks when you swap implementations. Use the `IFeatureFlagStore` interface for evaluation. If you need programmatic flag definition, make it conditional on the store type or use a separate seeding mechanism.

---

## 9. Assuming Only `Enabled` Triggers a Change Notification

**Wrong assumption:**

```csharp
// Expecting a rollout change to go unnoticed because Enabled did not move
memoryStore.Define(new FeatureFlagDefinition
{
    Name = "feature-a",
    Enabled = true,
    Rules = [FeatureFlagRule.Percentage(20)]   // was Percentage(10)
});
// Assuming no notification here, and polling the store instead
```

**Right understanding:**

```csharp
// InMemoryFeatureFlagStore emits a change whenever the definition differs, not only on Enabled:
memoryStore.Define(new FeatureFlagDefinition { Name = "feature-a", Enabled = true });
memoryStore.Define(new FeatureFlagDefinition { Name = "feature-a", Enabled = false });
// Notification: WasEnabled=true, IsEnabled=false

memoryStore.Define(new FeatureFlagDefinition
{
    Name = "feature-a", Enabled = true, Rules = [FeatureFlagRule.Percentage(10)]
});
memoryStore.Define(new FeatureFlagDefinition
{
    Name = "feature-a", Enabled = true, Rules = [FeatureFlagRule.Percentage(20)]
});
// Notification as well -- the rollout percentage changed, even though Enabled did not
```

**Why:** the two built-in stores differ, and the difference matters when you watch for changes.

| Store | Emits a change when… |
|-------|----------------------|
| `InMemoryFeatureFlagStore` | any part of the definition differs: `Enabled`, `Description`, the rule count, or a rule's `Type` / `Enabled` / `Values` (compared element-wise, so `["10"] → ["20"]` and `["tenant-a"] → ["tenant-b"]` both count) |
| `ConfigurationFeatureFlagStore` | only the flag's `Enabled` state transitions (including a flag disappearing from configuration, reported as enabled → disabled) |

Two consequences to keep in mind:

- `FeatureFlagChange` only carries `WasEnabled` / `IsEnabled`. A notification triggered by a rule edit
  reports the same value in both, so treat the event as "this flag's definition moved, re-read it" rather
  than as a description of what changed.
- Defining a flag for the first time never emits a change — there is no previous state to transition from.

---

## 10. Not Setting Up Environment in Context

**Wrong:**

```csharp
// Environment rules always match or never match
Rules =
[
    new FeatureFlagRule { Type = "environment", Values = ["production"], Enabled = false },
]

// But context never includes Environment
var context = new FeatureFlagContext { TenantId = "acme" };
// Environment rule is skipped (context.Environment is null) -- feature may unexpectedly be enabled
```

**Right:**

```csharp
// Include Environment in context
var context = new FeatureFlagContext
{
    TenantId = tenant.TenantId,
    UserId = user.UserId,
    Environment = hostEnvironment.EnvironmentName  // "Development", "Staging", "Production"
};

// Or centralize via IFeatureFlagContextProvider
public class HttpFeatureFlagContextProvider(
    ITenantContext tenant,
    IHostEnvironment env) : IFeatureFlagContextProvider
{
    public Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default)
        => Task.FromResult(new FeatureFlagContext
        {
            TenantId = tenant.TenantId,
            Environment = env.EnvironmentName
        });
}
```

**Why:** When a context property is null, rules targeting that property are skipped (they do not match). This means an environment rule with `Values = ["production"]` does nothing when `context.Environment` is null -- the evaluator moves to the next rule or falls back to the global state. Always populate all relevant context dimensions, especially `Environment` if you use environment-gated rules.
