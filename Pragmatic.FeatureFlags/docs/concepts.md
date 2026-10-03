# Architecture and Core Concepts

This guide explains **why** Pragmatic.FeatureFlags exists, how the evaluation engine works, and how it integrates with the rest of the Pragmatic.Design ecosystem. Read this before diving into the individual feature guides.

---

## The Problem

Applications need to control feature visibility at runtime -- for gradual rollouts, A/B testing, beta programs, and kill switches. Without a feature flag system, you end up with one of these patterns.

### Compile-time flags that require redeployment

```csharp
public class CheckoutService
{
    // Changing this requires a code change + redeployment
    private const bool UseNewCheckout = false;

    public async Task ProcessAsync()
    {
        if (UseNewCheckout)
            await NewCheckoutFlowAsync();
        else
            await LegacyCheckoutFlowAsync();
    }
}
```

To toggle the feature, you redeploy the application. No gradual rollout. No per-tenant control. No kill switch without downtime.

### Configuration-based flags that lack targeting

```csharp
public class CheckoutService(IConfiguration config)
{
    public async Task ProcessAsync()
    {
        // Simple on/off -- same for all users
        var useNew = config.GetValue<bool>("Features:NewCheckout");

        if (useNew)
            await NewCheckoutFlowAsync();
        else
            await LegacyCheckoutFlowAsync();
    }
}
```

Configuration gives you runtime toggling, but it is a flat key-value store. The same flag value applies to every user, every tenant, every environment. You cannot enable a feature for enterprise tenants only, or roll it out to 10% of users, or gate it to staging before production.

### Hand-rolled targeting with scattered logic

```csharp
public class CheckoutService(IConfiguration config, ITenantContext tenant, ICurrentUser user)
{
    public async Task ProcessAsync()
    {
        var betaTenants = config.GetSection("Features:NewCheckout:BetaTenants").Get<string[]>();
        var betaUsers = config.GetSection("Features:NewCheckout:BetaUsers").Get<string[]>();
        var rolloutPercent = config.GetValue<int>("Features:NewCheckout:RolloutPercent");

        var isEnabled = betaTenants?.Contains(tenant.TenantId) == true
            || betaUsers?.Contains(user.UserId) == true
            || ComputeHash(user.UserId) % 100 < rolloutPercent;

        if (isEnabled)
            await NewCheckoutFlowAsync();
        else
            await LegacyCheckoutFlowAsync();
    }
}
```

Now you have targeting, but the evaluation logic is duplicated across every feature check. Each developer implements hashing differently. Percentage rollouts are inconsistent. There is no central view of what is enabled for whom.

### The fundamental issues

1. **Compile-time flags** require redeployment to toggle.
2. **Configuration flags** are flat on/off -- no targeting, no gradual rollout.
3. **Hand-rolled targeting** is scattered, inconsistent, and hard to audit.
4. **No change notification** -- when a flag changes, downstream caches and UI need to react.

---

## The Solution

Pragmatic.FeatureFlags provides a rules-based evaluation engine that separates flag definition from evaluation context. Flags have ordered targeting rules. Evaluation takes a context (tenant, user, plan, environment, custom properties) and returns a deterministic result.

```csharp
// 1. Define the flag with targeting rules
memoryStore.Define(new FeatureFlagDefinition
{
    Name = "new-checkout",
    Enabled = false,  // globally disabled
    Rules =
    [
        // Enable for enterprise tenants
        new FeatureFlagRule { Type = "tenant", Values = ["acme", "contoso"], Enabled = true },
        // Enable for 20% of all users (gradual rollout)
        new FeatureFlagRule { Type = "percentage", Values = ["20"], Enabled = true }
    ]
});

// 2. Evaluate with context
var context = new FeatureFlagContext { TenantId = "acme", UserId = "user-42" };
var isEnabled = await store.IsEnabledAsync("new-checkout", context);
// Result: true (matched tenant rule for "acme")
```

Rules are evaluated in order -- first match wins. If no rule matches, the global `Enabled` state is the fallback. The evaluation engine handles deterministic percentage rollouts, case-insensitive matching, and custom property rules.

---

## How It Works

### The Evaluation Pipeline

Every feature flag check flows through the same deterministic pipeline:

```
IsEnabledAsync("new-checkout", context)
    |
    v
Lookup flag definition in store
    |
    +-- Not found --> return false (safe default)
    |
    v
Evaluate rules in order (first match wins)
    |
    +-- Rule 1: type="tenant", values=["acme","contoso"]
    |   context.TenantId = "acme" --> MATCH --> return rule.Enabled (true)
    |
    +-- Rule 2: type="percentage", values=["20"]
    |   (skipped -- Rule 1 already matched)
    |
    v
No rule matched --> return definition.Enabled (global state)
```

**Key behaviors:**

- **Unknown flags return `false`** -- safe default, no exception.
- **First match wins** -- rule order matters. Put specific rules before broad rules.
- **Null context values skip the rule** -- a `tenant` rule does not match when `context.TenantId` is null. The evaluator moves to the next rule.
- **Case-insensitive matching** -- `"ACME"` matches `"acme"` in all list rules.

### Rule Types

| Rule Type | Values | Matches When |
|-----------|--------|-------------|
| `tenant` | `["acme", "contoso"]` | `context.TenantId` is in the list (case-insensitive) |
| `user` | `["user-1", "user-2"]` | `context.UserId` is in the list (case-insensitive) |
| `plan` | `["enterprise", "pro"]` | `context.Plan` is in the list (case-insensitive) |
| `environment` | `["staging", "development"]` | `context.Environment` is in the list (case-insensitive) |
| `percentage` | `["20"]` | Deterministic SHA256 hash of `{flagName}:{userId}` falls in the bucket |
| `property` | `["region", "eu-west", "eu-east"]` | `context.Properties["region"]` is in `["eu-west", "eu-east"]` |

Unknown rule types are silently skipped -- they do not match and do not error.

### Percentage Rollout (Deterministic Bucketing)

Percentage rules use SHA256 to hash the flag name and user/tenant identifier into a stable bucket:

```
hash = SHA256("{flagName}:{userId or tenantId or 'anonymous'}")
bucket = |hash| % 100

if bucket < percentage --> rule matches
```

**Properties of deterministic bucketing:**

| Property | Behavior |
|----------|----------|
| **Deterministic** | Same user + same flag = same result every time |
| **Independent** | Different flags have different distributions (flag name is part of the seed) |
| **Progressive** | Going from 10% to 20% adds users -- never removes existing ones |
| **Fallback** | If neither `UserId` nor `TenantId` is set, `"anonymous"` is used as the seed |
| **Boundaries** | 0% = always disabled, 100% = always enabled |

This means you can safely increase the rollout percentage without "churning" users. A user in the 10% bucket is always in the 20% bucket too.

**The boundaries are absolute, and ignore the rule's own `Enabled`.** They are shortcuts, not ordinary rules:

| Percentage | Result | `rule.Enabled` | Later rules / global `Enabled` |
|-----------|--------|----------------|-------------------------------|
| `<= 0` | `false` for everyone | ignored | not consulted |
| `>= 100` | `true` for everyone | ignored | not consulted |
| in between, user **in** bucket | `rule.Enabled` | honoured | not consulted (first match wins) |
| in between, user **outside** bucket | no match | n/a | evaluation continues |

`Percentage(0)` is therefore a kill switch: it wins over the flag's global `Enabled` and over every rule
that follows it. If you want "nobody for now, but let the rest of the rules decide", omit the percentage
rule instead of setting it to zero.

### Property Rules

For targeting beyond the built-in fields, use property rules with custom key-value pairs:

```csharp
// Define: enable for EU regions
new FeatureFlagRule
{
    Type = "property",
    Values = ["region", "eu-west", "eu-east"],  // [0]=key, [1..]=values
    Enabled = true
}

// Evaluate with context
var context = new FeatureFlagContext
{
    Properties = new Dictionary<string, string>
    {
        ["region"] = "eu-west"
    }
};
// Result: matches (eu-west is in allowed values)
```

The first element in `Values` is the property key; the remaining elements are the allowed values. The match is case-insensitive. If the property key is not in the context, the rule does not match (moves to the next rule).

---

## IFeatureFlag: Strongly-Typed Flags

Instead of using magic strings, define flags as types for compile-time safety:

```csharp
public sealed class LoyaltyDiscount : IFeatureFlag
{
    public static string Name => "loyalty-discount";
    public static string? Description => "10% loyalty discount -- gradual rollout";
}

// Usage: no magic string
var isEnabled = await store.IsEnabledAsync<LoyaltyDiscount>(context, ct);

// Compare to string-based:
var isEnabled = await store.IsEnabledAsync("loyalty-discount", context, ct);
```

The `IFeatureFlag` interface uses static abstract members:

```csharp
public interface IFeatureFlag
{
    static abstract string Name { get; }
    static abstract string? Description { get; }
}
```

The `FeatureFlagStoreExtensions` class provides generic overloads on `IFeatureFlagStore`:

| Extension Method | Description |
|-----------------|-------------|
| `IsEnabledAsync<TFlag>(ct)` | Evaluate typed flag without context |
| `IsEnabledAsync<TFlag>(context, ct)` | Evaluate typed flag with targeting context |
| `GetDefinitionAsync<TFlag>(ct)` | Get full definition of typed flag |

**When to use typed flags:** For flags that are checked in multiple places, strongly-typed flags prevent typos and enable IDE navigation. For one-off checks or dynamic flag names (e.g., from configuration), string-based API is fine.

---

## IFeatureFlagStore: The Store Abstraction

All feature flag operations go through `IFeatureFlagStore`:

```csharp
public interface IFeatureFlagStore
{
    Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default);
    Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext context, CancellationToken ct = default);
    Task<FeatureFlagDefinition?> GetDefinitionAsync(string flagName, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureFlagDefinition>> GetAllAsync(CancellationToken ct = default);
    IAsyncEnumerable<FeatureFlagChange> WatchAsync(CancellationToken ct = default);
}
```

The store is responsible for both **storage** and **evaluation**. This design allows backends (database, LaunchDarkly, Azure App Configuration) to implement evaluation natively if they support it, rather than forcing everything through the in-memory evaluator.

### InMemoryFeatureFlagStore

The default store for development and testing:

- Uses `ConcurrentDictionary<string, FeatureFlagDefinition>` with case-insensitive keys.
- Delegates evaluation to `FeatureFlagEvaluator` (internal).
- Emits `FeatureFlagChange` via `Channel<T>` when `Define()` changes a flag's global `Enabled` state.
- Additional methods: `Define(FeatureFlagDefinition)` and `Remove(string)`.

### Custom Store Implementations

Implement `IFeatureFlagStore` for production backends:

```csharp
public class DatabaseFeatureFlagStore(IDbConnectionFactory db) : IFeatureFlagStore
{
    public async Task<bool> IsEnabledAsync(
        string flagName, FeatureFlagContext context, CancellationToken ct = default)
    {
        var definition = await GetDefinitionAsync(flagName, ct);
        if (definition is null)
            return false;  // Unknown flag = disabled

        // Reuse the evaluation logic or implement custom
        return FeatureFlagEvaluator.Evaluate(definition, context);
    }

    // ... other methods
}
```

**Note:** `FeatureFlagEvaluator` is `internal` to the `Pragmatic.FeatureFlags` package. Custom stores must either replicate the evaluation logic or load definitions and implement their own rule processing.

See [Stores](stores.md) for detailed store documentation.

---

## FeatureFlagContext: Targeting Information

The context carries the targeting dimensions for evaluation:

```csharp
public sealed record FeatureFlagContext
{
    public string? TenantId { get; init; }
    public string? UserId { get; init; }
    public string? Plan { get; init; }
    public string? Environment { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; }
}
```

| Property | Type | Description |
|----------|------|-------------|
| `TenantId` | `string?` | Current tenant identifier |
| `UserId` | `string?` | Current user identifier |
| `Plan` | `string?` | Subscription plan (e.g., `"enterprise"`, `"pro"`, `"free"`) |
| `Environment` | `string?` | Runtime environment (e.g., `"staging"`, `"production"`) |
| `Properties` | `IReadOnlyDictionary<string, string>` | Custom key-value properties for property rules |

Use `FeatureFlagContext.Empty` when no context is available. This is equivalent to `new FeatureFlagContext()` and means only the global `Enabled` state and percentage rules (with `"anonymous"` seed) are evaluated.

---

## IFeatureFlagContextProvider: Automatic Context Resolution

For applications where the context comes from ambient state (HTTP request, tenant, user), implement `IFeatureFlagContextProvider`:

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

**Important:** The bridging between `ITenantContext` and `FeatureFlagContext.TenantId` is **manual**. You must explicitly copy the values. There is no automatic wiring between the multi-tenancy and feature flags systems.

### Evaluating against the ambient context: `IFeatureFlags`

Registering a provider is only half the job: something has to *call* it. `IFeatureFlags` is that half:
it pairs the store with the registered provider, so business code asks the question directly.

```csharp
// Without it, the context plumbing shows up at every call site:
var context = await contextProvider.GetContextAsync(ct);
var allowed = await store.IsEnabledAsync<EarlyCheckIn>(context, ct);

// With it:
var allowed = await flags.IsEnabledAsync<EarlyCheckIn>(ct);
```

`AddPragmaticFeatureFlags()` registers it (scoped, since a provider usually reads per-request state).
No provider registered? Evaluation falls back to `FeatureFlagContext.Empty`: the global state and
percentage rules still apply, targeting rules simply do not match: a conservative default rather than
a failure.

Use `IFeatureFlagStore` directly when you need to evaluate against a context that is *not* the ambient
one (a background job acting on behalf of another tenant, a preview of "what would this user see").
`IFeatureFlags.GetContextAsync()` exposes the ambient context when you want to evaluate several flags
against the same one, or to log what a decision was based on.

---

## WatchAsync: Change Notifications

`IFeatureFlagStore.WatchAsync` returns an `IAsyncEnumerable<FeatureFlagChange>` for real-time change notifications:

```csharp
await foreach (var change in store.WatchAsync(cancellationToken))
{
    logger.LogInformation(
        "Flag '{Flag}' changed: {Was} -> {Is} at {Time}",
        change.FlagName,
        change.WasEnabled,
        change.IsEnabled,
        change.Timestamp);

    // Invalidate caches, update UI, trigger recomputation, etc.
}
```

The `FeatureFlagChange` record:

```csharp
public sealed record FeatureFlagChange(
    string FlagName,
    bool WasEnabled,
    bool IsEnabled,
    DateTimeOffset Timestamp);
```

**In `InMemoryFeatureFlagStore`:** a change is emitted whenever `Define()` alters **any part** of an
existing definition: `Enabled`, `Description`, the rule count, or a rule's type, `Enabled` or values.
Defining a flag for the first time emits nothing.

**In `ConfigurationFeatureFlagStore`:** only `Enabled` transitions are emitted (including a flag
disappearing from configuration, reported as enabled → disabled).

**Delivery:** changes are broadcast: each watcher has its own channel, so every concurrent watcher sees
every change instead of competing for them. Both the pending buffer (used while nobody is watching) and
each watcher's queue are bounded and drop the oldest entry when full, so a process that never watches does
not accumulate changes indefinitely.

**In custom stores:** The implementation determines when and how changes are detected (polling, database notifications, webhooks, etc.).

---

## Rule Composition Patterns

### Beta program: specific tenants + percentage rollout

```csharp
Rules =
[
    new FeatureFlagRule { Type = "plan", Values = ["enterprise"], Enabled = true },
    new FeatureFlagRule { Type = "percentage", Values = ["10"], Enabled = true }
]
// Enterprise tenants: always enabled (rule 1 matches first)
// Everyone else: 10% rollout (rule 2)
```

### Kill switch for a specific tenant

```csharp
Rules =
[
    new FeatureFlagRule { Type = "tenant", Values = ["legacy-corp"], Enabled = false },
]
// Global Enabled = true
// Everyone gets the feature except "legacy-corp"
```

### Environment gate with user allowlist

```csharp
Rules =
[
    new FeatureFlagRule { Type = "environment", Values = ["production"], Enabled = false },
    new FeatureFlagRule { Type = "user", Values = ["admin-1"], Enabled = true },
]
// In production: disabled for everyone (rule 1 matches)
// Outside production: only admin-1 gets it (rule 2 matches)
// Everyone else outside production: falls through to global Enabled
```

### Region-based targeting

```csharp
Rules =
[
    new FeatureFlagRule
    {
        Type = "property",
        Values = ["region", "eu-west", "eu-east"],
        Enabled = true
    }
]
// Global Enabled = false
// Only EU users see the feature
```

**Rule ordering tip:** Put specific deny rules first, then specific allow rules, then broad percentage rules. The first match wins, so a deny rule before a percentage rule prevents specific entities from ever being in the rollout.

---

## DI Registration

### Default (InMemoryFeatureFlagStore)

```csharp
services.AddPragmaticFeatureFlags();
// Registers InMemoryFeatureFlagStore as IFeatureFlagStore (TryAdd, singleton)
```

When the SG detects `Pragmatic.FeatureFlags` in the project references, it auto-registers this call. No manual registration is needed for basic usage.

### Custom Store

```csharp
// Generic overload -- always replaces (AddSingleton, not TryAdd)
services.AddPragmaticFeatureFlags<MyDatabaseFeatureFlagStore>();

// Or manual registration (register before AddPragmaticFeatureFlags)
services.AddSingleton<IFeatureFlagStore, MyCustomStore>();
services.AddPragmaticFeatureFlags();  // TryAdd won't replace your store
```

### Store Lifetime

The store is registered as singleton by default. Flag definitions are global; the evaluation context is per-call. This is appropriate because the store holds the flag definitions, not per-request state.

---

## Ecosystem Integration

### Multi-Tenancy

`FeatureFlagContext.TenantId` enables per-tenant feature targeting. The bridging is manual -- your `IFeatureFlagContextProvider` must copy `ITenantContext.TenantId` into the context.

See [Pragmatic.MultiTenancy](../../Pragmatic.MultiTenancy/README.md).

### Identity

`FeatureFlagContext.UserId` enables per-user targeting. Copy from `ICurrentUser.UserId`.

See [Pragmatic.Identity](../../Pragmatic.Identity/README.md).

### Configuration

`Pragmatic.FeatureFlags` and `Pragmatic.Configuration` are separate systems:

| System | Purpose | Evaluation |
|--------|---------|-----------|
| `IConfigurationStore` | Key-value settings (connection strings, limits) | Simple lookup |
| `IFeatureFlagStore` | Feature toggles with targeting | Context-based rules |

Use Configuration for settings that apply uniformly. Use FeatureFlags for features that need targeting, rollout, or per-tenant/user control.

See [Pragmatic.Configuration](../../Pragmatic.Configuration/README.md).

### Actions and Endpoints

Check flags in action handlers or endpoint `HandleAsync` to gate features at the business logic level:

```csharp
public override async Task<Result<CheckoutResult>> HandleAsync(CancellationToken ct)
{
    var context = new FeatureFlagContext { TenantId = tenantContext.TenantId };
    if (await flags.IsEnabledAsync("new-checkout", context, ct))
        return await NewCheckoutFlowAsync(ct);
    else
        return await LegacyCheckoutFlowAsync(ct);
}
```

See [Pragmatic.Actions](../../Pragmatic.Actions/README.md) and [Pragmatic.Endpoints](../../Pragmatic.Endpoints/README.md).

### Composition (SG Auto-Registration)

When the SG detects `Pragmatic.FeatureFlags` in project references (`HasFeatureFlags` in `DetectedFeatures`), it auto-registers `services.AddPragmaticFeatureFlags()`. No manual registration needed.

See [Pragmatic.Composition](../../Pragmatic.Composition/README.md).

---

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| Separate from Configuration | Feature flags need context-based evaluation, not simple key-value lookup |
| First-match-wins rule evaluation | Simple, predictable -- order matters, easy to reason about |
| Deterministic percentage rollout | SHA256 hash ensures same user always gets same bucket for same flag |
| Flag name in hash seed | Different flags have independent distributions -- no correlation |
| Unknown flags return `false` | Safe default -- unrecognized flags are disabled |
| Case-insensitive matching | Prevents subtle bugs from casing differences in tenant/user IDs |
| `IAsyncEnumerable` for WatchAsync | Natural streaming pattern, no callback registration needed |
| No rule = global state | Simple flags (on/off) don't need rules at all |
| Store handles evaluation | Backends can implement evaluation natively (e.g., LaunchDarkly SDK) |
| `IFeatureFlag` with static abstract members | Compile-time safety without runtime allocation for the flag type |

---

## Layer Dependencies

```
Layer 0 (Foundation)     Layer 1 (Capabilities)
|-- Result               |-- Configuration
|-- Ensure               |-- FeatureFlags  <-- this module
+-- Abstractions         |-- Validation
                         +-- ...
```

`Pragmatic.FeatureFlags` depends only on `Pragmatic.Abstractions` (for the interface types) and `Microsoft.Extensions.DependencyInjection.Abstractions`. It has no dependency on ASP.NET Core, Persistence, or any other Pragmatic module.

---

## See Also

- [Getting Started](getting-started.md) -- step-by-step setup
- [Stores](stores.md) -- IFeatureFlagStore, InMemoryFeatureFlagStore, custom backends
- [Common Mistakes](common-mistakes.md) -- avoid the most frequent pitfalls
- [Troubleshooting](troubleshooting.md) -- problem/solution guide
- [Pragmatic.MultiTenancy](../../Pragmatic.MultiTenancy/README.md) -- per-tenant targeting integration
- [Pragmatic.Configuration](../../Pragmatic.Configuration/README.md) -- key-value settings (complementary system)
