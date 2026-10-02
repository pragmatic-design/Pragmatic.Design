# Feature Flag Stores

Pragmatic.FeatureFlags uses a pluggable store architecture. The `IFeatureFlagStore` interface handles both storage and evaluation of feature flags. This is different from `IConfigurationStore` (simple key-value) -- feature flags require context-based evaluation with targeting rules, percentage rollout, etc.

## IFeatureFlagStore Interface

Defined in `Pragmatic.Abstractions`:

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

| Method | Description |
|--------|-------------|
| `IsEnabledAsync(flagName)` | Evaluate without context (uses defaults). Unknown flags return `false`. |
| `IsEnabledAsync(flagName, context)` | Evaluate with targeting context (tenant, user, plan, etc.) |
| `GetDefinitionAsync(flagName)` | Get the full definition including rules. Returns `null` for unknown flags. |
| `GetAllAsync()` | List all defined flags, ordered by name. |
| `WatchAsync()` | Stream of `FeatureFlagChange` records for real-time change notification. |

## InMemoryFeatureFlagStore

The default store for development and testing. Uses `ConcurrentDictionary<string, FeatureFlagDefinition>` with case-insensitive keys.

### Registration

```csharp
// Default: in-memory store
services.AddPragmaticFeatureFlags();
```

### Additional Methods

Beyond `IFeatureFlagStore`, the in-memory store exposes:

| Method | Description |
|--------|-------------|
| `Define(FeatureFlagDefinition)` | Create or update a flag definition |
| `Remove(string flagName)` | Remove a flag definition. Returns `true` if the flag existed. |

### Evaluation

The `InMemoryFeatureFlagStore` delegates to `FeatureFlagEvaluator` (internal), which processes rules in order and falls back to the global `Enabled` state if no rule matches.

### Change Notification

When `Define()` changes **any part** of an existing definition — `Enabled`, `Description`, the rule count,
or a rule's type, `Enabled` or values (compared element-wise) — a `FeatureFlagChange` is emitted to
`WatchAsync()` consumers. Defining a flag for the first time emits nothing: there is no previous state to
transition from.

Note that `ConfigurationFeatureFlagStore` is narrower: it emits only when a flag's `Enabled` state
transitions. See [common mistakes](common-mistakes.md#9-assuming-only-enabled-triggers-a-change-notification).

Changes are **broadcast**: each watcher gets its own channel, so every concurrent watcher observes every
change rather than competing for them. Two buffers bound the memory this can hold:

| Buffer | Size | On overflow |
|--------|------|-------------|
| Pending (no watcher yet) | 64 | oldest change dropped |
| Per watcher | 1024 | oldest change dropped for that watcher |

Changes published while nobody is watching are delivered to the first watcher that arrives — a flag
defined during startup seeding is still observable by a watcher started afterwards.

`Define()` throws `ArgumentException` if a rule carries a type the evaluation engine does not know: such a
rule would never match, so the flag would silently behave as if it were absent. Validate rules coming from
an external source with `FeatureFlagRule.IsKnownType`.

## DI Registration Options

```csharp
// Default: in-memory store (via TryAdd)
services.AddPragmaticFeatureFlags();

// Custom store type
services.AddPragmaticFeatureFlags<MyDatabaseFeatureFlagStore>();

// Manual registration (register before AddPragmaticFeatureFlags)
services.AddSingleton<IFeatureFlagStore, MyCustomStore>();
services.AddPragmaticFeatureFlags(); // TryAdd won't replace your store
```

The generic overload `AddPragmaticFeatureFlags<TStore>()` uses `AddSingleton` (not `TryAdd`), so it always replaces any previous registration.

## Implementing a Custom Store

To back feature flags with a database, remote service, or other backend:

```csharp
public class DatabaseFeatureFlagStore(
    IDbConnectionFactory db,
    ILogger<DatabaseFeatureFlagStore> logger) : IFeatureFlagStore
{
    public async Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default)
        => await IsEnabledAsync(flagName, FeatureFlagContext.Empty, ct);

    public async Task<bool> IsEnabledAsync(
        string flagName, FeatureFlagContext context, CancellationToken ct = default)
    {
        var definition = await GetDefinitionAsync(flagName, ct);
        if (definition is null)
            return false; // Unknown flag = disabled

        // Use the built-in evaluator or implement custom evaluation logic
        return FeatureFlagEvaluator.Evaluate(definition, context);
    }

    public async Task<FeatureFlagDefinition?> GetDefinitionAsync(
        string flagName, CancellationToken ct = default)
    {
        // Load from database
    }

    public async Task<IReadOnlyList<FeatureFlagDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        // Load all from database, ordered by name
    }

    public async IAsyncEnumerable<FeatureFlagChange> WatchAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Poll for changes, use database notifications, etc.
    }
}
```

**Note**: `FeatureFlagEvaluator` is `internal` to the `Pragmatic.FeatureFlags` package. Custom stores that want to reuse the built-in rule evaluation logic should either: (a) duplicate the evaluation logic, or (b) store definitions and delegate `IsEnabledAsync` to loading the definition and evaluating rules manually.

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Separate from `IConfigurationStore` | Feature flags need context-based evaluation, not simple key-value lookup |
| Unknown flags return `false` | Safe default -- unrecognized flags are disabled |
| Case-insensitive flag names | Prevents subtle bugs from casing differences |
| `IAsyncEnumerable` for `WatchAsync` | Natural streaming pattern, no callback registration needed |
| Store is singleton by default | Flag definitions are global; evaluation context comes from the caller |
