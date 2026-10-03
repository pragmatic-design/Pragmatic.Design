---
title: "Configuration Stores"
description: "Pragmatic.Configuration provides a pluggable store architecture for runtime configuration and secrets. The store layer is separate from the compile-time `[Confi"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Configuration/docs/stores.md
sidebar:
  order: 3
---
Pragmatic.Configuration provides a pluggable store architecture for runtime configuration and secrets. The store layer is separate from the compile-time `[Configuration]` binding -- it handles dynamic values, cascade resolution, hot-reload, and tenant overrides.

## Store Interfaces

All interfaces are defined in `Pragmatic.Abstractions` so any module can depend on them without pulling in the full runtime.

### IConfigurationStore

Backend-agnostic key-value store with tenant isolation and change notification:

```csharp
public interface IConfigurationStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default);
    Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default);
    Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default);
    IAsyncEnumerable<ConfigurationChange> WatchAsync(string keyPattern, CancellationToken ct = default);
}
```

Key methods:
- **`GetAsync`** -- reads a single value, optionally scoped to a tenant.
- **`GetSectionAsync`** -- reads all values under a prefix (e.g., `"Booking:"` returns all Booking keys).
- **`SetAsync` / `DeleteAsync`** -- mutate values, optionally tenant-scoped.
- **`WatchAsync`** -- streams `ConfigurationChange` records for real-time change notification. Accepts a key pattern (e.g., `"Booking:*"`).

### ISecretStore

Read-only store for secrets. Secrets are set out-of-band (vault, CI/CD, user-secrets):

```csharp
public interface ISecretStore
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default);
}
```

Implementations manage encryption at rest. The `InMemorySecretStore` stores secrets in plain text (development only); production implementations (Database, Azure Key Vault) encrypt at rest.

### Configuration Caching

Configuration values read from remote backends are cached via `ICacheStack` (from `Pragmatic.Abstractions`) using the `CacheCategories.Configuration` category. This provides key-prefix isolation and per-category TTL configuration.

Default: an internal `InMemoryConfigurationCacheStack` (ConcurrentDictionary with TTL). When `Pragmatic.Caching` is registered, it automatically picks up the `HybridCacheStack` backend with L1 memory + L2 distributed caching.

### ConfigurationChange

Record emitted by `IConfigurationStore.WatchAsync`:

```csharp
public sealed record ConfigurationChange(
    string Key,
    string? OldValue,
    string? NewValue,
    string? TenantId,
    DateTimeOffset Timestamp);
```

## Built-In Stores

### InMemoryConfigurationStore

Default store for development and testing. Uses `ConcurrentDictionary` for thread-safe access and `Channel<ConfigurationChange>` for change notification.

Features:
- Tenant isolation via separate `ConcurrentDictionary<string, ConcurrentDictionary<string, string>>`.
- Tenant-scoped `GetAsync`/`GetSectionAsync` return **only** the tenant's overrides (or `null` / an
  empty section); the cascade to base is the resolver's job, not the store's.
- `SetAsync` emits a `ConfigurationChange`; each `WatchAsync` subscriber only receives changes whose
  key matches the prefix it subscribed with (filtered at broadcast time).

### InMemorySecretStore

Default secret store for development and testing. Stores secrets in plain text. Supports tenant-scoped secrets with fallback to base.

Additional method for programmatic setup:

```csharp
var secretStore = (InMemorySecretStore)store;
secretStore.SetSecret("ApiKey", "my-secret-key");
secretStore.SetSecret("ApiKey", "tenant-specific-key", tenantId: "acme");
```

## Cascade Resolution

`ConfigurationResolver` resolves values through user, tenant and environment layers. It is registered as **scoped** because `ITenantContext` and `ICurrentUser` are scoped.

Constructor:

```csharp
public ConfigurationResolver(
    IConfigurationStore store,
    EnvironmentProfile environment,
    ITenantContext? tenantContext = null,
    ICurrentUser? currentUser = null)
```

### Resolution Order

Precedence, highest first: **user → tenant → environment overlay → base**.

```
1. User override      (scoped to the current authenticated user, via ICurrentUser)
2. Tenant override    (if the tenant context is resolved)
3. Environment layer  (most specific to least, via ResolutionChain)
4. Base value         (key = "MaxRetries")
```

For a single key (`ResolveAsync`), the first layer that produces a value wins: user → tenant →
environment chain (most specific to least) → base.

For a section (`ResolveSectionAsync`), overlays are applied least-specific to most-specific so the
highest-precedence scope wins on a key collision: base → environment → tenant → user.

The store returns **only** the scoped override for a `GetAsync(key, scopeId)` call; it does not fall
back to base itself, so a tenant (or user) without an override for a key still lets the environment
overlay win.

### EnvironmentProfile

Wraps the current environment with Pragmatic conventions:

```csharp
var profile = EnvironmentProfile.From("Staging", "eu-west");
// profile.Name = "Staging"
// profile.Tag = "eu-west"
// profile.ResolutionChain = ["base", "staging", "staging-eu-west"]
```

The resolution chain determines environment overlay order:
- `"base"` is always first.
- Environment name is added (lowercased) unless Production.
- Environment + tag is added if a tag is specified.

Production has a shorter chain: `["base"]` (no environment overlay).

Helper properties: `IsDevelopment`, `IsStaging`, `IsProduction`, `IsTesting`, `IsEnvironment(name)`.

## Bridge to IOptionsMonitor

`PragmaticConfigurationProvider` bridges `IConfigurationStore` into Microsoft's `IConfiguration` system, enabling `IOptions<T>` and `IOptionsMonitor<T>` to read from any Pragmatic store backend.

### Setup

```csharp
builder.Configuration.AddPragmaticStore(store, environmentProfile, keyPrefix: null);
```

### How It Works

1. **Startup**: `Load()` reads all values from the store (base + environment layers).
2. **Background**: Subscribes to `WatchAsync()` for change notifications.
3. **On change**: Re-loads all values and calls `OnReload()`, which triggers `IOptionsMonitor<T>` change callbacks.
4. **Key normalization**: `/` is replaced with `:` for Microsoft Configuration compatibility.

The watcher runs as a fire-and-forget task with exception handling:
- Reload failures are logged but do not kill the watcher.
- `OperationCanceledException` is expected on dispose.
- Unexpected watch stream failures are logged and the watcher stops.

### Hot-Reload in Practice

```csharp
public class MyService(IOptionsMonitor<BookingOptions> options)
{
    public int MaxGuests => options.CurrentValue.MaxGuests;
    // Automatically picks up changes from any backend
}
```

When a value changes in the store:
1. Store emits `ConfigurationChange` via `WatchAsync`.
2. `PragmaticConfigurationProvider` re-loads all data.
3. `IOptionsMonitor<T>` fires change callbacks.
4. `options.CurrentValue` returns updated values.

## Runtime Reconfiguration

### Writing Configuration Values

```csharp
public class ConfigService(IConfigurationStore store)
{
    public async Task UpdateMaxGuests(int newValue, CancellationToken ct)
    {
        await store.SetAsync("Booking:MaxGuests", newValue.ToString(), ct: ct);
        // If PragmaticConfigurationProvider is registered, IOptionsMonitor
        // will pick up the change automatically via hot-reload.
    }

    public async Task SetTenantOverride(string tenantId, int maxGuests, CancellationToken ct)
    {
        await store.SetAsync("Booking:MaxGuests", maxGuests.ToString(), tenantId, ct);
    }
}
```

### Multi-Tenant Configuration

Enable in `AddPragmaticConfiguration`:

```csharp
services.AddPragmaticConfiguration(options =>
{
    options.MultiTenant.Enabled = true;
    options.MultiTenant.FallbackToBase = true;
});
```

When enabled, `ConfigurationResolver` reads `ITenantContext` from DI to resolve tenant-specific overrides. If the tenant has no override and `FallbackToBase` is true (the default), the environment and base values are returned; with `false` they are not read for a resolved tenant, and the value is absent unless a user override supplies it.

## Backend Packages

### Database Backend

ADO.NET-based, zero EF Core dependency. Supports PostgreSQL, SQL Server, and SQLite.

```bash
dotnet add package Pragmatic.Configuration.Database
```

Its tables are auto-created: `pragmatic_config`, `pragmatic_secrets` (AES-256-GCM encrypted), and the shared audit trail's (`Pragmatic.Audit`), which records every change.

### Azure Backend

Azure App Configuration for dynamic config, Azure Key Vault for secrets.

```bash
dotnet add package Pragmatic.Configuration.Azure
```

Authentication uses `DefaultAzureCredential`. Keys follow Azure naming conventions with configurable prefix.

See the [README](/modules/configuration-module/overview/) for full backend configuration options.

## Implementing a Custom Store

Implement `IConfigurationStore` and/or `ISecretStore`:

```csharp
public class RedisConfigurationStore : IConfigurationStore
{
    // Implement all interface methods
    // Register before AddPragmaticConfiguration() so TryAdd doesn't override
}

services.AddSingleton<IConfigurationStore, RedisConfigurationStore>();
services.AddPragmaticConfiguration(); // TryAdd won't replace your store
```

The in-memory defaults use `TryAdd`, so any store registered before `AddPragmaticConfiguration()` takes precedence.
