# Pragmatic.Configuration.Samples

Runnable samples for [Pragmatic.Configuration](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Runs each scenario in turn (real DI + stores), prints output to stdout, and exits.

## Scenarios

| # | Sample | Runs against |
|---|--------|--------------|
| 1 | `InMemoryStoreSample` — get/set/delete, sections, multi-tenant overrides | in-memory (self-contained) |
| 2 | `HotReloadSample` — `WatchAsync` change stream | in-memory (self-contained) |
| 3 | `BridgeSample` — `AddPragmaticStore()` → `IConfiguration` + `IOptionsMonitor<T>` hot-reload | in-memory (self-contained) |
| 4 | `CascadeResolutionSample` — `EnvironmentProfile` chain + `ConfigurationResolver` (tenant→env→base) | in-memory (self-contained) |
| 5 | `DatabaseSample` — database store + schema auto-create + audit log | SQLite in-memory (self-contained) |
| 6 | `EncryptionSample` — AES-256-GCM database secret store + key validation | SQLite in-memory (self-contained) |
| 7 | `AzureSample` — App Configuration + Key Vault registration | **setup only** — live calls need a real Azure resource |
| 8 | `ManagementActionsSample` — `SetConfigValue` / `GetConfigValue` / `GetConfigAuditLog` | SQLite in-memory (self-contained) |
| 9 | `CacheStackSample` — read-through configuration caching via `ICacheStack` | in-memory (self-contained) |

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Configuration/samples/Pragmatic.Configuration.Samples/Pragmatic.Configuration.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

All samples are self-contained except **#7 `AzureSample`**, which only demonstrates registration and the resolved client types — it does not contact Azure. To exercise live App Configuration / Key Vault calls, set real `AppConfigurationEndpoint` / `KeyVaultUri` values, authenticate via `DefaultAzureCredential` (managed identity, Azure CLI login, or environment credentials), and uncomment the live calls at the bottom of the file.

The database and encryption samples use an in-memory SQLite database (`Microsoft.Data.Sqlite`); no external server is needed.

## Related

- Module: [Pragmatic.Configuration](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/configuration/
- Source: `Pragmatic.Configuration/src/Pragmatic.Configuration/`
