# Pragmatic.Migrations.Samples

Runnable samples for [Pragmatic.Migrations](../..).

> ⚠️ **Preview** — samples illustrate the module's intended usage. APIs may change between preview versions.

## Entrypoint

Console application. Prints scenario output to stdout and exits.

## How to run

From the repo root:

```bash
dotnet run --project Pragmatic.Migrations/samples/Pragmatic.Migrations.Samples/Pragmatic.Migrations.Samples.csproj
```

## Prerequisites

- .NET 10 SDK (see `global.json`)
- No external infrastructure required unless noted below

## External dependencies

_None by default._ Samples that require external infrastructure (RabbitMQ, Kafka, PostgreSQL, SMTP server, etc.) document their setup here — typically via a `compose.yml` alongside this README.

## Scenarios

- `FreshDatabaseSample` — fresh database to V1 schema
- `EvolveSchemaSample` — diff V1 → V2 and apply the changes
- `IdempotentRerunSample` — same schema twice produces an empty diff (safe to boot on every startup)
- `DataMigrationSample` — `IDataMigration` runs once, tracked by name in `__PragmaticDataMigrations`
- `ExcludeTableSample` — externally-owned tables stay put when listed in `ExcludeTable`
- `MultiProviderSample` — same `SchemaDiff`, three provider-specific SQL outputs
- `MigrationHookSample` — `IMigrationHook` around a single change, in the migration transaction
- `MigrationOptionsSample` — DryRun, Force, database filter, audit table
- `ConcurrentIndexSample` — post-commit index builds (`CONCURRENTLY` / `ONLINE = ON`)
- `LeaderElectionSample` — `__PragmaticLock`, leader and follower behaviour
- `TenantMigrationSample` — `TenantMigrationOrchestrator` across DB-per-tenant databases
- `CliCommandsSample` — what each `pragmatic-migrate` command does
- `ClientGenerationSample` — manifest export and typed-client generation
- `DesiredSchemas` — V1/V2 fixtures used by the other scenarios

## Related

- Module: [Pragmatic.Migrations](../..)
- Documentation: https://www.pragmaticdesign.net/docs/modules/migrations/
- Source: `Pragmatic.Migrations/src/Pragmatic.Migrations/`
