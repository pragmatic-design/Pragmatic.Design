---
name: pragmatic-use-configuration
description: Use when defining typed settings, when a rule spans two settings, or when values must change at runtime per tenant or user — Pragmatic.Configuration, [Configuration], [ConfigInvariant], cascading stores, secrets, backends.
---

# Pragmatic Use Configuration

**Covers:** Configuration with Pragmatic.Configuration — [Configuration] classes bound, validated ([ConfigInvariant]) and registered by the generator; runtime stores with a user → tenant → environment → base cascade, change handlers, secrets, backends and an admin package.

Two pillars: **compile-time binding** (`[Configuration]` → binding + validation + DI) and **runtime
stores** (cascade user → tenant → environment → base, change notification, pluggable backends). Most
applications need only the first; reach for the second when a value must change without a redeploy.

## Packages

```xml
<PackageReference Include="Pragmatic.Configuration" Version="1.0.0-alpha.*" />
<PackageReference Include="Pragmatic.SourceGenerator" Version="1.0.0-alpha.*">
  <IncludeAssets>analyzers; build; buildtransitive</IncludeAssets><PrivateAssets>all</PrivateAssets>
</PackageReference>
<!-- runtime backends, one per store you use -->
<PackageReference Include="Pragmatic.Configuration.Database" Version="1.0.0-alpha.*" />
<!-- admin actions over the store -->
<PackageReference Include="Pragmatic.Configuration.Management" Version="1.0.0-alpha.*" />
```

## Compile-time binding

```csharp
[Configuration]                       // binds section "Booking", validates on start, registers in DI
public partial class BookingOptions
{
    [Required]      public string CurrencyCode { get; init; } = "";
    [Range(1, 365)] public int MinStayDays { get; init; } = 1;
    [Range(1, 365)] public int MaxStayDays { get; init; } = 30;

    [Sensitive]     public string ChannelApiKey { get; init; } = "";

    [ConfigInvariant("MinStayDays cannot exceed MaxStayDays: no stay would be bookable.")]
    public bool TheMinimumFitsUnderTheMaximum() => MinStayDays <= MaxStayDays;
}
```

- Inject `IOptions<BookingOptions>`, `IOptionsSnapshot<T>` (per request) or `IOptionsMonitor<T>`
  (follows reloads) — the generator registers the standard Options wrappers, not the bare type.
- The section is the class name without `Options` (`BookingOptions` → `Booking`), or
  `[Configuration(SectionPath = "Services:OrderApi")]`. `ValidateOnStart = false` turns the start-up
  check off — rarely what you want: a missing `[Required]` value then fails at first use instead.
- The class must be `partial` (**PRAG2000**, with a code fix) and neither static nor abstract
  (**PRAG2001**). **PRAG2050**: a `[Required]` property with an initializer — the default makes the
  requirement unfalsifiable.
- The Pragmatic host calls every assembly's generated registration itself — nothing to write in
  `Program.cs`.

### `[ConfigInvariant]` — the rule between two settings

DataAnnotations check each property alone: 400 and 300 each pass `[Range(1, 500)]`, and together they
may describe a configuration that accepts nothing. A `[ConfigInvariant("message")]` method is run by a
generated `IValidateOptions<T>` at start-up, so a contradictory pair is a refusal to start, not an
empty result in production.

The method must be a **parameterless instance method returning `bool`**, `true` meaning valid; any
other shape is **PRAG2002** (error), because the validator could not call it. Test both sides:
the bad pair refused, and a good pair accepted (a validator that refuses everything also "refuses the
bad pair").

### `[Sensitive]`

Marks a value that must not appear **in clear in the change history**. The generator emits an
`ISensitiveKeyClassifier` from the declarations, and the stores ask it before writing the audit entry.
It governs **audit masking, not encryption**: a value that must be encrypted at rest belongs in a
secret store (below), with the configuration holding a `secret://` reference. To be warned when a
`[Sensitive]` key is written to a store as plaintext, call
`services.DecorateConfigurationStoreWithSensitiveGuard()` after registering the backend — opt-in, and
it warns without blocking.

## Runtime stores

```csharp
// IStartupStep.ConfigureServices
services.AddPragmaticConfiguration(o =>
{
    o.MultiTenant.Enabled = true;      // read the tenant layer — off by default
    o.EnvironmentTag = "eu";           // optional: adds "{environment}-eu" to the chain
});
services.AddDatabaseConfigurationStore(o =>
{
    o.Provider = DatabaseProvider.PostgreSql;
    o.ConnectionString = connectionString;
    o.ProviderFactory = NpgsqlFactory.Instance;   // the ADO.NET provider that opens it
});
```

Without a backend both stores are in memory. Read-through caching is on by default
(`EnableReadCaching`; secrets for `SecretCacheTtl`, 60 s) and wraps whichever backend is registered
last.

**Call `AddPragmaticConfiguration` once, with your options.** A second call with options throws — the
resolver keeps the first call's, so the second's would be ignored; a second call without options is a
no-op.

⚠️ **A module that imports the management package** (below) makes the runtime services a requirement of
**every host that includes it**. A host with no runtime configuration of its own still has to call
`AddPragmaticConfiguration()`, or container validation stops it at start-up for want of an
`IConfigurationResolver`.

### Reading: the cascade

Inject **`IConfigurationResolver`** (scoped): `ResolveAsync(key)`, `ResolveSectionAsync(prefix)`, and
`ResolveWithTraceAsync(key)`, which returns the value **and the layer that supplied it** — the answer to
"why is this value what it is?". Or **`ITenantOptions<T>`** for a `[Configuration]` class bound for the
current tenant.

| Layer (highest first) | Stored as | Read when |
|---|---|---|
| user | scope `user:{userId}` | an `ICurrentUser` is in the scope |
| tenant | scope `{tenantId}` | `MultiTenant.Enabled` and the tenant is resolved |
| environment | key `{env}/{key}`, then `{env}-{tag}/{key}` | `env` is the lower-cased host environment; **Production has no overlay** |
| base | key `{key}` | always |

⚠️ The tenant layer is read only with `MultiTenant.Enabled = true`. Without it a per-tenant value in the
store is never consulted, and every tenant gets the environment's. `MultiTenant.FallbackToBase`
(default `true`) decides what a resolved tenant with no override of its own gets: `false` and the
environment and base layers are not read for it — the value is absent, a user override still applies.

Writing: `IConfigurationStore.SetAsync(key, value, tenantId, ct)` / `DeleteAsync`. ⚠️ The store does not
validate keys, and some backends interpret them (SQL, Redis patterns, paths): a key that comes from
user input is checked against an allow-list first.

### Reacting to a change

```csharp
services.AddConfigurationChangeHandler<BookingOptions, BookingOptionsChanged>();

public sealed class BookingOptionsChanged(ILogger<BookingOptionsChanged> log)
    : IConfigurationChangeHandler<BookingOptions>
{
    public Task OnChangedAsync(ConfigurationChange change, CancellationToken ct = default)
    { /* change.Key, OldValue, NewValue, TenantId, Timestamp — re-read through the resolver */ }
}
```

Called for any key under the options' section. It needs a store whose `WatchAsync` emits changes: the
database store polls (`PollingInterval`, 30 s; `EnableChangePolling`), and
`AddPostgresConfigurationWatch()` (`Pragmatic.Configuration.Database.Postgres`) adds a native push on
top of the poll.

### Store values into `IOptionsMonitor<T>`

The bridge makes the store a source of `IConfiguration`, so `[Configuration]` classes see runtime
values and `IOptionsMonitor<T>` follows them:

```csharp
builder.Configuration.AddPragmaticStore(store, EnvironmentProfile.From(env.EnvironmentName), keyPrefix: "Booking");
```

It applies the environment chain, not the tenant or user layer — `IOptions` has no request scope.
Per-tenant values are read through `ITenantOptions<T>` or the resolver.

### Backends

| Package | Registration |
|---|---|
| `Pragmatic.Configuration.Database` | `AddDatabaseConfigurationStore(o => …)`, `AddDatabaseSecretStore()` |
| `Pragmatic.Configuration.Redis` | `AddRedisConfigurationStore(o => …)` |
| `Pragmatic.Configuration.Consul` | `AddConsulConfigurationStore(o => …)` |
| `Pragmatic.Configuration.Kubernetes` | `AddKubernetesConfigurationStore(o => …)`, `AddKubernetesSecretStore(o => …)` |
| `Pragmatic.Configuration.Azure` | `AddAzureAppConfigurationStore(o => …)`, `AddAzureKeyVaultSecretStore(o => …)`, or both with `AddAzureConfiguration(o => …)` |
| `Pragmatic.Configuration.Aws` | `AddAwsParameterStore(o => …)`, `AddAwsSecretStore(o => …)` |
| `Pragmatic.Configuration.Gcp` | `AddGcpSecretStore(o => …)` |
| `Pragmatic.Configuration.Vault` | `AddVaultSecretStore(o => …)` |
| `Pragmatic.Agent.Client` | `UseAgent()` replaces the configuration store with the Agent's shared KV, falling back to the previous one when the Agent is unreachable — `pragmatic-use-distributed` |

**Database backend.** Tables `pragmatic_config`, `pragmatic_secrets` and the shared audit trail's
(`__AuditEntries`, `__AuditSegments`, `__PrunedRanges`), created on the store's first use
(`AutoCreateSchema`, default on). Every change is written to that trail in the same transaction, as a hash
of the previous value — never the value itself. The store connects with `ConnectionString` and `ProviderFactory` (`NpgsqlFactory.Instance`,
`SqlClientFactory.Instance`, `SqliteFactory.Instance`); an `IDbConnectionFactory` the application registers
wins over both. With neither, resolving the store fails naming what to set.

## Secrets

Secrets live in an `ISecretStore` (read) / `IWritableSecretStore` (write — resolving it against a
read-only backend throws with a clear message). A configuration value `secret://{key}` is resolved from
the secret store at read time, so the configuration store — and whatever replicates it — holds the
pointer, never the material.

Database secrets are AES-256-GCM. The key: `DatabaseConfigurationOptions.EncryptionKey` (base64,
32 bytes) or the `PRAGMATIC_SECRET_KEY` environment variable by default;
`UseEncryptionKeyFromEnvironment("NAME")` or `UseEncryptionKeyFromSecretStore("name")` (e.g. Key Vault)
after `AddDatabaseSecretStore()`. Rotation: add the old key to `PreviousEncryptionKeys`, set the new one,
run `ISecretKeyRotationService` to re-encrypt.

`services.AddRequiredConfiguration("Payments:ApiKey")` refuses to start while a raw key that no options
class covers is unset.

## Exposing it: the management package

`Pragmatic.Configuration.Management` ships the admin operations as actions. Import the package into one
of your boundaries and expose only what the application needs:

```csharp
[UsePackage<ConfigurationManagementPackage, AdminBoundary>]
[ExposeEndpoint<GetConfigValues>(HttpVerb.Get, "configuration/values")]
[ExposeEndpoint<GetConfigResolution>(HttpVerb.Get, "configuration/resolution")]
public sealed class AdminModule;
```

| Action | Permission |
|---|---|
| `GetConfigValue`, `GetConfigValues` (`Prefix`, `TenantId`, `Limit` = 500), `GetConfigResolution` | `configuration.values.read` |
| `SetConfigValue` (`Key`, `Value`, `TenantId`) | `configuration.values.write` |
| `DeleteConfigValue` | `configuration.values.delete` |
| `GetConfigAuditLog` (`KeyPrefix`, `TenantId`, `Limit` = 50) | `configuration.audit.read` |

`SetConfigValue` writes only a **declared** setting: a property of a `[Configuration]` section in the
host's catalogue (`Section:Property`), or a key beneath one (`Section:List:0`), matched without regard to
case; the key has to be segments of letters, digits, `_`, `.`, `-` separated by `:`. Anything else is a
400 naming the key, and with no `[Configuration]` section in the host nothing is writable. ⚠️ Within that,
the holder of `configuration.values.write` can change every declared setting — including repointing a
`secret://` reference: expose writes deliberately, or wrap them in an action of yours that narrows the set.

**The actions are bound to the caller's tenant** (`TenantBinding` in `Pragmatic.Authorization`). A
caller that belongs to a tenant writes, deletes and reads **only that tenant's** values; an empty
`TenantId` is refused for writes and deletes, because the base value is what every tenant inherits, and
refused for the audit log, where it means every tenant's changes. Reading the base values stays open: the
tenant inherits them anyway. A caller with no tenant — a platform operator — is unrestricted. Refusals
are `403`.

## Testing

- Invariants and classification are plain code: build the options, run the generated validator, assert
  both the refused and the accepted case.
- A value read while services are being registered — a choice of backend, a flag that decides a
  registration — goes through `UseSetting` on the `WebApplicationFactory`: `ConfigureAppConfiguration`
  arrives after that read (`pragmatic-use-testing`).
- Cascade behaviour: seed the in-memory store with a base, an environment and a tenant value, then
  assert `ResolveWithTraceAsync` reports the layer you expect.

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Showcase and Warehouse example
applications — code that compiles and that `Showcase.IntegrationTests` and `Warehouse.IntegrationTests`
exercise — and kept identical to it by the gate: an options class with `[Sensitive]` and a
`[ConfigInvariant]`, a setting read through `IOptionsMonitor<T>` so it changes at runtime, and the
management package exposed read-only.
