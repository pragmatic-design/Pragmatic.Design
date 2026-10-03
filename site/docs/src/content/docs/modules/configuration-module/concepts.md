---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Configuration exists, how its two pillars work (compile-time binding and runtime stores), and how the pieces compose into "
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Configuration/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Configuration exists, how its two pillars work (compile-time binding and runtime stores), and how the pieces compose into a complete configuration system. Read this before the individual feature guides.

---

## The Problem

Configuration management in .NET starts simple and grows painful. Each options class requires the same boilerplate. Secrets are handled differently in each environment. Dynamic configuration changes require custom plumbing. Multi-tenant overrides are an afterthought.

### Boilerplate per options class

Every `IOptions<T>` class requires identical registration code:

```csharp
// BookingOptions -- 3 properties, 12 lines of wiring
services.AddOptions<BookingOptions>()
    .Bind(configuration.GetSection("Booking"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// PaymentOptions -- same pattern, different names
services.AddOptions<PaymentOptions>()
    .Bind(configuration.GetSection("Payment"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// NotificationOptions -- and again...
services.AddOptions<NotificationOptions>()
    .Bind(configuration.GetSection("Notification"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Three options classes, 36 lines of identical structure. Add validation attributes and it grows further. Now multiply by 20 options classes in a real application.

### Missing validation

Validation is opt-in and easy to forget:

```csharp
// Forgot .ValidateDataAnnotations() -- [Required] attributes are ignored
services.AddOptions<PaymentOptions>()
    .Bind(configuration.GetSection("Payment"));
    // App starts with empty PaymentGatewayUrl, fails at runtime
```

### Secrets management varies by environment

```csharp
// Development: user-secrets
// Staging: environment variables
// Production: Azure Key Vault
// Each environment has different secret access patterns
// No unified API, no tenant isolation, no encryption at rest for DB-backed configs
```

### No dynamic reconfiguration

Changing a config value requires a deployment. Runtime changes (A/B testing, feature rollouts, emergency overrides) need custom infrastructure that is rarely built.

### Per-tenant overrides are ad-hoc

```csharp
// Typical approach: hardcoded switch
var maxGuests = tenantId switch
{
    "acme" => 200,
    "contoso" => 50,
    _ => config.MaxGuests  // default
};
// Not scalable, not auditable, not dynamic
```

### The consequences

| Problem | Impact |
|---------|--------|
| Boilerplate per options class | Developer fatigue, copy-paste errors |
| Forgotten validation | Runtime failures from invalid config |
| No unified secrets API | Different code paths per environment |
| Static configuration | Deployments for every config change |
| No tenant isolation | Hard-coded overrides, no audit trail |
| Reflection-based binding | AOT/trimming incompatibility |

---

## The Solution

Pragmatic.Configuration solves this with two pillars:

### Pillar 1: Compile-time binding (Source Generator)

Annotate a class with `[Configuration]`. The source generator produces all the boilerplate:

```csharp
[Configuration]
public partial class BookingOptions
{
    [Required]
    [MaxLength(100)]
    public string HotelName { get; set; } = "";

    [Range(1, 100)]
    public int MaxGuests { get; set; } = 10;
}
```

The SG generates:
- Per-type extension: `AddBookingOptions(services, configuration)` that calls `Bind`, `ValidateDataAnnotations`, and `ValidateOnStart`.
- Per-assembly aggregator: `Add{Prefix}Configuration(services, configuration)` that calls every individual `Add*Options` method.
- Section path inference: `BookingOptions` binds to `"Booking"` (removes `Options` suffix).

One line in your startup registers everything:

```csharp
services.AddMyAppConfiguration(configuration);
```

No boilerplate. No forgotten validation. No reflection at runtime (the binding code is source-generated).

### Pillar 2: Runtime stores (Dynamic configuration)

A pluggable store architecture for dynamic values, cascade resolution, and hot-reload:

```csharp
services.AddPragmaticConfiguration(options =>
{
    options.EnvironmentTag = "eu-west";
    options.MultiTenant.Enabled = true;
});
```

This registers `IConfigurationStore` and `ISecretStore` with cascade resolution (base -> environment -> tenant -> user) and bridges into Microsoft's `IOptionsMonitor<T>` for hot-reload.

---

## How It Works

### Architecture overview

```
  Compile-time (SG)                    Runtime (Stores)
  ==================                   ==================
  [Configuration] attribute            IConfigurationStore
       |                               ISecretStore
       v                                    |
  Source Generator                          v
       |                              ConfigurationResolver
       v                              (cascade: base -> env -> tenant -> user)
  AddOptions<T>()                           |
  .Bind(section)                            v
  .ValidateDataAnnotations()          PragmaticConfigurationProvider
  .ValidateOnStart()                  (bridge to IConfiguration)
       |                                    |
       v                                    v
  IOptions<T>                         IOptionsMonitor<T>
  IOptionsSnapshot<T>                 (hot-reload)
  IOptionsMonitor<T>
```

The two pillars are independent. You can use the source generator without runtime stores (static config from `appsettings.json`). You can use runtime stores without the source generator (manual `IOptions<T>` binding). Together, they provide the complete solution.

---

## Pillar 1: Source Generator Details

### The `[Configuration]` attribute

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConfigurationAttribute : Attribute
{
    public string? SectionPath { get; init; }      // Override inferred path
    public bool ValidateOnStart { get; init; } = true;  // Fail fast on invalid config
}
```

The class **must** be `partial` (the SG emits code into it). It cannot be `static` or `abstract`.

### Section path inference

The section path is inferred by removing the `Options` suffix from the class name:

| Class Name | Inferred Section |
|------------|-----------------|
| `BookingOptions` | `"Booking"` |
| `PaymentOptions` | `"Payment"` |
| `MyConfig` | `"MyConfig"` (no suffix to remove) |

Override explicitly when the convention does not match:

```csharp
[Configuration(SectionPath = "Services:OrderApi")]
public partial class OrderApiOptions { ... }
```

### What the SG generates

For each `[Configuration]` class, the SG generates a per-type extension method:

```csharp
// Generated: BookingOptionsConfigurationExtensions.g.cs
public static class BookingOptionsConfigurationExtensions
{
    public static IServiceCollection AddBookingOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BookingOptions>()
            .Bind(configuration.GetSection("Booking"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }
}
```

And a per-assembly aggregator that calls all individual methods:

```csharp
// Generated: MyAppConfigurationExtensions.g.cs
public static class MyAppConfigurationExtensions
{
    public static IServiceCollection AddMyAppConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddBookingOptions(configuration);
        services.AddPaymentOptions(configuration);
        services.AddNotificationOptions(configuration);
        return services;
    }
}
```

### Supported validation attributes

The SG detects `System.ComponentModel.DataAnnotations` attributes and generates `.ValidateDataAnnotations()`:

- `[Required]`
- `[Range]`
- `[MaxLength]`, `[MinLength]`, `[StringLength]`
- `[RegularExpression]`
- `[EmailAddress]`, `[Phone]`, `[Url]`

Properties with the `required` keyword are treated as required even without `[Required]`.

### Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| PRAG2000 | Error | `[Configuration]` class must be declared as `partial` |
| PRAG2001 | Error | `[Configuration]` class cannot be static or abstract |
| PRAG2050 | Warning | Property has `[Required]` but also has a default value |

---

## Pillar 2: Runtime Stores

### Store interfaces

Both interfaces are defined in `Pragmatic.Abstractions`, so any module can depend on them without pulling in the full runtime.

**IConfigurationStore** -- backend-agnostic key-value storage:

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

**ISecretStore** -- read-only secrets vault:

```csharp
public interface ISecretStore
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default);
}
```

Secrets are set out-of-band (vault UI, CI/CD, deployment scripts). The `ISecretStore` only reads.

### Built-in backends

| Backend | Package | Config Store | Secret Store | Best For |
|---------|---------|-------------|-------------|----------|
| In-Memory | `Pragmatic.Configuration` | `InMemoryConfigurationStore` | `InMemorySecretStore` | Development, testing |
| Database | `Pragmatic.Configuration.Database` | `DatabaseConfigurationStore` | `DatabaseSecretStore` | Self-hosted, full control |
| Azure | `Pragmatic.Configuration.Azure` | `AzureAppConfigurationStore` | `AzureKeyVaultSecretStore` | Azure deployments |

The in-memory stores are registered via `TryAdd`, so any backend registered before `AddPragmaticConfiguration()` takes precedence.

---

## Cascade Resolution

`ConfigurationResolver` resolves values across four layers. Precedence, highest first:
**user → tenant → environment overlay → base**.

### Resolution order

```
1. User override      (scoped to the current authenticated user, via ICurrentUser)
2. Tenant override    (scoped to the current tenant, via ITenantContext)
3. Environment layer  (environment / environment+tag prefixes, most specific first)
4. Base value         (key = "MaxRetries")
```

The user and tenant scopes reuse the store's scoped-key mechanism
(`GetAsync(key, scopeId)`). The user scope id is namespaced as `user:{userId}` so user
overrides never collide with tenant overrides in the same store.

### EnvironmentProfile

Wraps the current environment into a resolution chain:

```csharp
var profile = EnvironmentProfile.From("Staging", "eu-west");
// profile.ResolutionChain = ["base", "staging", "staging-eu-west"]
```

The resolution chain determines overlay order:
- `"base"` is always first.
- Environment name (lowercased) is added unless Production.
- Environment + tag is added if a tag is specified.

Production has a shorter chain: `["base"]` -- no environment overlay, because production values are the base values.

### How it works

For a single key (`ResolveAsync`), the first layer that produces a value wins:

1. Check the user override (returns immediately if found).
2. Check the tenant override (returns immediately if found).
3. Walk the environment chain from most specific to least specific.
4. Fall back to the base value.

For a section (`ResolveSectionAsync`), overlays are applied least-specific to most-specific so the
highest-precedence scope wins on a key collision:

1. Load base values.
2. Overlay environment-specific values (general to specific).
3. Overlay tenant-specific values.
4. Overlay user-specific values last.

```csharp
// Example: resolving "MaxRetries" in staging-eu-west for user "u1" of tenant "acme"
// 1. Check store.GetAsync("MaxRetries", "user:u1")      -- user override
// 2. Check store.GetAsync("MaxRetries", "acme")         -- tenant override
// 3. Check store.GetAsync("staging-eu-west/MaxRetries") -- env+tag
// 4. Check store.GetAsync("staging/MaxRetries")         -- env
// 5. Check store.GetAsync("MaxRetries")                 -- base
```

Store implementations return **only** the scoped override for a `GetAsync(key, scopeId)` call (or
`null`); they do not fall back to base themselves. The resolver owns the cascade, so a tenant that
has no override for a key still lets the environment overlay win.

### Multi-tenant configuration

Enable tenant isolation:

```csharp
services.AddPragmaticConfiguration(options =>
{
    options.MultiTenant.Enabled = true;
    options.MultiTenant.FallbackToBase = true;
});
```

When enabled, `ConfigurationResolver` reads `ITenantContext` from DI (from [Pragmatic.MultiTenancy](/modules/multi-tenancy/overview/)) to resolve tenant-specific overrides. If `FallbackToBase` is `true` (the default) and no tenant override exists, the environment and base values are returned; with `false` they are not read for a resolved tenant.

---

## Bridge and Hot-Reload

`PragmaticConfigurationProvider` bridges `IConfigurationStore` into Microsoft's `IConfiguration` pipeline. This enables `IOptions<T>` and `IOptionsMonitor<T>` to read from any Pragmatic store backend.

### Setup

```csharp
builder.Configuration.AddPragmaticStore(store, environmentProfile, keyPrefix: null);
```

### How it works

1. **Startup:** `Load()` reads all values from the store (base + environment layers).
2. **Background:** Subscribes to `WatchAsync()` for change notifications.
3. **On change:** Re-loads all values and calls `OnReload()`, which triggers `IOptionsMonitor<T>` change callbacks.
4. **Key normalization:** `/` is replaced with `:` for Microsoft Configuration compatibility.

The watcher runs in the background with robust error handling:
- Reload failures are logged but do not kill the watcher.
- `OperationCanceledException` is expected on dispose.
- Unexpected watch stream failures are logged and the watcher stops.

### Hot-reload in practice

```csharp
public class BookingService(IOptionsMonitor<BookingOptions> options)
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

---

## Database Backend

ADO.NET-pure -- zero EF Core dependency. Supports PostgreSQL, SQL Server, and SQLite via `ISqlDialect`.

### Registration

```csharp
services.AddDatabaseConfigurationStore(options =>
{
    options.Provider = DatabaseProvider.PostgreSql;
    options.ConnectionString = connectionString;
    options.ProviderFactory = NpgsqlFactory.Instance;   // the ADO.NET provider that opens it
    options.AutoCreateSchema = true;
    options.Environment = "staging";
    options.AuditUser = "system";
    options.PollingInterval = TimeSpan.FromSeconds(30);
});

// Optional: encrypted secret store
services.AddDatabaseSecretStore();
```

The package references no ADO.NET provider, so the application hands it one: `ProviderFactory` with
`ConnectionString`, or an `IDbConnectionFactory` of its own (`DbConnection CreateConnection()`), which
wins over both. With neither, resolving the store fails naming what to set.

### Schema

The store's two tables are auto-created, and the shared audit trail's beside them (idempotent via
`ConfigurationSchemaManager`):

| Table | Purpose |
|-------|---------|
| `pragmatic_config` | Key-value pairs with `tenant_id`, `environment`, `version` |
| `pragmatic_secrets` | Encrypted values (AES-256-GCM) with tenant isolation |
| `__AuditEntries`, `__AuditSegments`, `__PrunedRanges` | Every change on the shared trail of `Pragmatic.Audit`, in the same transaction as the change: who, when, which key, and a hash of the previous value, never the value itself |

### SQL dialect abstraction

Each provider has different upsert, timestamp, and constraint semantics:

| Provider | Upsert | Timestamps | Encryption Column |
|----------|--------|------------|-------------------|
| PostgreSQL | `ON CONFLICT DO UPDATE` | `TIMESTAMPTZ` | `BYTEA` |
| SQL Server | `MERGE` | `DATETIMEOFFSET` | `VARBINARY(MAX)` |
| SQLite | `ON CONFLICT` + COALESCE sentinel | `TEXT` | `BLOB` |

SQLite uses `COALESCE(tenant_id, '')` in unique indexes because SQLite treats `NULL != NULL` in unique constraints.

### Encryption

Secrets are encrypted at rest with AES-256-GCM:

```
Format: [12-byte nonce][16-byte auth tag][ciphertext]
```

- Random nonce per encryption (no nonce reuse).
- 32-byte (256-bit) key, base64-encoded.
- Key from `DatabaseConfigurationOptions.EncryptionKey` or `PRAGMATIC_SECRET_KEY` environment variable.
- Authenticated encryption: tampering is detected.

---

## Azure Backend

Azure App Configuration for dynamic config, Azure Key Vault for secrets.

### Registration

```csharp
// Both stores at once
services.AddAzureConfiguration(options =>
{
    options.AppConfigurationEndpoint = "https://myapp.azconfig.io";
    options.KeyVaultUri = "https://myapp-vault.vault.azure.net";
    options.KeyPrefix = "Pragmatic";
    options.CacheExpiration = TimeSpan.FromSeconds(30);
    options.SecretCacheExpiration = TimeSpan.FromMinutes(5);
});

// Or individually
services.AddAzureAppConfigurationStore(options => { ... });
services.AddAzureKeyVaultSecretStore(options => { ... });
```

### Key conventions

| Concept | Azure Format |
|---------|-------------|
| Config key | `{prefix}:{key}` |
| Environment label | `{environment}` or `{environment}-{tag}` |
| Tenant label | `tenant-{tenantId}` |
| Secret name | `{prefix}--{key}` (`:` replaced by `--` for Key Vault) |
| Tenant secret | `{prefix}--tenant--{tenantId}--{key}` |
| Sentinel key | `{prefix}:Sentinel` (change detection trigger) |

Authentication uses `DefaultAzureCredential` (managed identity, Azure CLI, etc.) or explicit connection strings.

---

## Configuration Caching

Configuration values read from remote backends are cached via `ICacheStack` (from [Pragmatic.Caching](/modules/caching/overview/)).

Default behavior:
- An internal `InMemoryConfigurationCacheStack` (ConcurrentDictionary with TTL) is registered via `TryAdd`.
- When `Pragmatic.Caching` is referenced, it automatically replaces this with `HybridCacheStack` (L1 memory + L2 distributed).

Cache TTLs:

| Category | Default TTL |
|----------|-------------|
| Configuration values | 30 seconds |
| Secrets | 5 minutes |
| Feature flags | 10 seconds |

---

## Implementing a Custom Store

Implement `IConfigurationStore` and/or `ISecretStore`:

```csharp
public class RedisConfigurationStore : IConfigurationStore
{
    // Implement all interface methods
}

// Register before AddPragmaticConfiguration() so TryAdd doesn't override
services.AddSingleton<IConfigurationStore, RedisConfigurationStore>();
services.AddPragmaticConfiguration(); // TryAdd won't replace your store
```

The in-memory defaults use `TryAdd`, so any store registered before `AddPragmaticConfiguration()` takes precedence.

---

## Consuming Options

Use standard Microsoft `IOptions<T>` patterns -- the generator and store system handle the wiring:

```csharp
// Real-time values (recommended for long-lived services)
public class BookingService(IOptionsMonitor<BookingOptions> options)
{
    public int MaxGuests => options.CurrentValue.MaxGuests;
}

// Per-request snapshot
public class BookingHandler(IOptionsSnapshot<BookingOptions> options) { }

// Singleton (reads once at startup)
public class StaticService(IOptions<BookingOptions> options) { }
```

| Pattern | Lifetime | Hot-Reload | Use Case |
|---------|----------|------------|----------|
| `IOptions<T>` | Singleton | No | Static config that never changes |
| `IOptionsSnapshot<T>` | Scoped | Per-request | Config that changes between requests |
| `IOptionsMonitor<T>` | Singleton | Yes (callbacks) | Config that changes during app lifetime |

---

## Ecosystem Integration

| Module | Integration |
|--------|-------------|
| [Pragmatic.Composition](/modules/composition/overview/) | `AddPragmaticConfiguration()` in `IStartupStep.ConfigureServices` |
| [Pragmatic.FeatureFlags](/modules/feature-flags/overview/) | Separate `IFeatureFlagStore` for evaluation-based flags (not key-value) |
| [Pragmatic.MultiTenancy](/modules/multi-tenancy/overview/) | `ITenantContext` feeds `ConfigurationResolver` for tenant overrides |
| [Pragmatic.Caching](/modules/caching/overview/) | Configuration values cached via `ICacheStack` |
| [Pragmatic.SourceGenerator](/source-generator/) | Generates `IOptions<T>` binding, validation, DI registration |

---

## See Also

| Topic | Location |
|-------|----------|
| Getting Started | [getting-started.md](/modules/configuration-module/getting-started/) |
| Store Backends | [stores.md](/modules/configuration-module/stores/) |
| Common Mistakes | [common-mistakes.md](/modules/configuration-module/common-mistakes/) |
| Troubleshooting | [troubleshooting.md](/modules/configuration-module/troubleshooting/) |
