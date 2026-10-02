# Pragmatic.Migrations

Declarative schema migrations for Pragmatic.Design — no migration files, no EF Core migrations, no manual SQL.

## How It Works

```
Compile Time                    Runtime
┌──────────────┐     ┌─────────────────────────────────────────┐
│ Source Gen    │     │ Introspect DB → Diff → SQL → Execute   │
│ ↓             │     │                                         │
│ SchemaVersion │────▶│ SchemaDiffEngine → SqlGenerator → Runner│
│ (desired)     │     │                                         │
└──────────────┘     └─────────────────────────────────────────┘
```

1. **Source Generator** reads `[Entity]` attributes and emits a `SchemaVersion` (desired schema) at compile time
2. **Introspector** reads the current database schema via `information_schema` / `sys.*` / `PRAGMA`
3. **Diff Engine** compares desired vs current, producing ordered `SchemaChange[]`
4. **SQL Generator** emits idempotent, provider-specific SQL (IF NOT EXISTS, DO $$ blocks)
5. **Runner** executes each change in a transaction — if one fails, everything rolls back

## Quick Start

```csharp
// Program.cs — zero configuration
await PragmaticApp.RunAsync(args, builder =>
{
    builder.UsePragmaticMigrations();
});
```

That's it. The SG detects `Pragmatic.Migrations` in your references and auto-generates the schema.

## Supported Providers

| Provider | Generator | Introspector | Idempotent SQL |
|----------|-----------|-------------|----------------|
| PostgreSQL | `DO $$ IF NOT EXISTS $$` blocks | `information_schema` + `pg_catalog` | Yes |
| SQL Server | `IF NOT EXISTS (sys.*)` checks | `sys.*` views | Yes |
| SQLite | `CREATE IF NOT EXISTS` + table rebuild | `sqlite_master` + `PRAGMA` | Yes |

Provider is auto-detected from `SchemaVersion.ProviderName` (set by the SG based on your EF Core provider).

SQLite has no `ALTER COLUMN` and cannot add or drop a foreign key on an existing table: those
changes are applied by rebuilding the table (rename → recreate with the target shape → copy the
columns that exist on both sides → drop). The runner does this for you, in the same transaction as
the rest of the migration.

## API

### Database Filtering

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.OnlyDatabase<ShowcaseAppDatabase>();  // Migrate only this DB
});
```

### Dry Run

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.DryRun();  // Generate SQL without executing
});
```

### Force Breaking Changes

By default, breaking changes (DROP TABLE, DROP COLUMN, SET NOT NULL, narrowing type changes) are blocked. Use `Force` to apply:

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.Force();  // Allow breaking changes at startup
});
```

At host startup a blocked breaking change — or any migration failure — **aborts startup**: the host will not run on a stale or partial schema. Inspect the failure, then either revise the entity change or opt in with `m.Force()` for a controlled deployment.

### Data Migrations

`IDataMigration` runs a named data transformation — backfilling a column, converting values, moving data between tables — exactly once per database. Each is tracked by name in `__PragmaticDataMigrations` and runs in its own transaction after the schema changes.

```csharp
public sealed class BackfillOrderStatus : IDataMigration
{
    public string Name => "2026-05_BackfillOrderStatus";  // stable, immutable — renaming re-runs it
    public int Order => 0;

    public async Task MigrateAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;  // required — keeps the change atomic with the tracking record
        cmd.CommandText = "UPDATE \"Orders\" SET \"Status\" = 'pending' WHERE \"Status\" IS NULL";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
```

```csharp
builder.UsePragmaticMigrations(m => m.AddDataMigration<BackfillOrderStatus>());
```

### Sharing a Database

By default a table found in the database but absent from your entities is proposed for `DROP` — a
breaking change, so startup is blocked rather than data deleted. When another system owns tables in
the same database:

```csharp
builder.UsePragmaticMigrations(m => m.ManageDeclaredTablesOnly());
```

Nothing this host did not declare is ever proposed for deletion. The trade-off: renaming an entity
leaves the old table behind, to be dropped by hand.

### Excluding Tables

Tables owned by another system, a DBA, or a database extension can be kept out of migration management — the diff engine will never propose to alter or drop them:

```csharp
builder.UsePragmaticMigrations(m => m.ExcludeTable("LegacyAudit", "spatial_ref_sys"));
```

### Concurrent Index Builds

For deployments where new indexes would lock large tables, opt into post-commit
out-of-transaction index creation. PostgreSQL emits `CREATE INDEX CONCURRENTLY`,
SQL Server emits `WITH (ONLINE = ON)` (Enterprise edition), SQLite is unaffected
(no concurrent mode — the flag is a no-op):

```csharp
builder.UsePragmaticMigrations(m => m.UseConcurrentIndexes());
```

The runner defers every `AddIndex` change to a phase that runs after the schema
transaction commits. A concurrent build that fails leaves the index INVALID on
the server (the schema is already committed) and the migration is reported as
failed with a suggestion to drop and recreate the index after fixing the
underlying cause.

## Error Handling

When a migration fails, the result includes:

- **Which change failed** (index + description)
- **The SQL that failed** (not the entire batch)
- **Context-aware suggestions** (e.g., "Column may contain NULL values — update before applying SET NOT NULL")
- **Transaction safety** — all changes are rolled back, database is in its original state

```
Migration failed at step 3/7: SET NOT NULL Orders.Email
Error: column "Email" of relation "Orders" contains null values

Suggestions:
  - Run with DryRun=true to inspect the generated SQL before applying
  - SET NOT NULL failed — the column may contain NULL values
  - Update existing NULL values before applying this change
  - All changes have been rolled back — the database is in its original state
```

## Schema Audit

Every successful migration is recorded in `__PragmaticSchema`:

| Column | Description |
|--------|-------------|
| `Hash` | Canonical identity of the schema — comparable with `MyDbSchema.Current.Hash` |
| `SchemaJson` | The full desired schema, serialized |
| `SqlScript` | The SQL that was executed |
| `ChangeCount` | Number of changes |
| `DurationMs` | Execution time |
| `AppliedAt` | Timestamp |
| `AppliedBy` | Machine name |

Rename it with `m.UseAuditTable("_MyMigrationHistory")` — both the table and its index are created
under that name.

> The audit table is a **record**, not an input. What the runner applies is decided by comparing the
> entities against the live database, so losing the audit history costs you the history, not
> correctness. `__PragmaticDataMigrations`, on the other hand, *is* an input: it is what makes an
> `IDataMigration` run exactly once.

`Hash` is the canonical identity of the schema, derived from the tables themselves — so
`SELECT "Hash" FROM "__PragmaticSchema" ORDER BY "AppliedAt" DESC LIMIT 1` answers "which schema
version is this database at?", and compares directly with `MyDbSchema.Current.Hash`.

## Schema Snapshot

The desired schema can be written to committed JSON files — one
`schema/<database>.schema.json` per database — so schema changes are reviewable in pull
requests and multi-branch schema conflicts surface as ordinary merge conflicts.

Generate it with the CLI:

```bash
pragmatic-migrate snapshot --assembly bin/Debug/net10.0/MyApp.dll --output schema
```

Or regenerate it automatically after every host build — opt in from the host project and
the `Pragmatic.Migrations` MSBuild target does the rest:

```xml
<PropertyGroup>
  <PragmaticSchemaSnapshot>true</PragmaticSchemaSnapshot>
</PropertyGroup>
```

### CI gate

Commit the `schema/` folder. In CI, regenerate the snapshot and fail the build when it
drifted from the entities:

```bash
pragmatic-migrate snapshot --assembly path/to/MyApp.dll --output schema
git diff --exit-code schema/
```

A non-empty diff means an entity changed without the snapshot being regenerated — or that
two branches changed the schema in conflicting ways.

## CLI Commands

The `pragmatic-migrate` CLI operates on a compiled host assembly. Beyond `apply`, `status`,
`script`, `snapshot`, and `history`, two commands support client and manifest workflows:

| Command | Description |
|---------|-------------|
| `manifest` | Exports the manifest JSON embedded in a compiled assembly to a file (`--assembly`, `--output`). |
| `generate` | Generates a typed client project from manifest data — C# (default) or TypeScript via `--language` (`--assembly` or `--manifest`, `--output`, optional `--namespace`/`--boundary`). |

```bash
# Export the manifest
pragmatic-migrate manifest --assembly bin/Debug/net10.0/MyApp.dll --output manifest.json

# Generate a typed C# client (use --language typescript for TS)
pragmatic-migrate generate --assembly bin/Debug/net10.0/MyApp.dll --output ./Client/
```

## Multi-Tenant Migrations

With DB-per-tenant enabled (`services.AddDbPerTenant(...)`), `ITenantMigrationOrchestrator` applies
the same desired schema to every active tenant that has a dedicated database — automatically, at
host startup, right after the host's own database has been migrated. A tenant failure aborts startup
just like a failure on the main database. Shared-database tenants are skipped.

Tune the sweep with `TenantMigrationOptions` (register your own instance before `AddDbPerTenant`):

| Option | Default | Effect |
|--------|---------|--------|
| `MaxParallelism` | 1 | Tenants migrated at the same time |
| `ContinueOnFailure` | false | Stop at the first failure; the rest are reported as skipped |
| `SuspendOnFailure` | true | Suspend a failed tenant (false leaves it `Migrating` for a retry) |
| `TenantTimeout` | 10 min | Bounds one tenant so a single unreachable database cannot stall the sweep |

`MigrateAllTenantsAsync` / `MigrateTenantAsync(tenantId, ...)` are also callable directly.

## Migration Hooks

`IMigrationHook` runs custom logic around individual schema changes — for inline data transformation
(populating new NOT NULL columns, converting types, moving data) tied to the change's transaction:

- `DatabaseName` — filter to a single database, or `null` for all.
- `BeforeChangeAsync(context, ct)` — return `false` to skip the change (default: proceed).
- `AfterChangeAsync(context, ct)` — runs after the change commits successfully; create commands via
  `MigrationStepContext.Connection` and set their `Transaction` to `MigrationStepContext.Transaction`
  so the data migration is atomic with the schema change.

## Comparison with EF Core Migrations

| Feature | EF Core Migrations | Pragmatic.Migrations |
|---------|-------------------|---------------------|
| Migration files | Yes (per-change) | No (declarative) |
| Design-time tools | Required (`dotnet ef`) | Not needed |
| ModelSnapshot | Required | Not needed (SG-generated) |
| Idempotent SQL | Optional (`--idempotent`) | Always |
| Multi-database | Manual | Auto-detected from topology |
| Transaction safety | Provider-dependent | Always (per-change execution) |
| Error diagnostics | Generic | Change-level with suggestions |
| Breaking change detection | No | Yes (blocks without Force) |
| Leader election | Not included | Built-in (database-based) |
| Tenant orchestration | Manual | Built-in, runs at startup |

## Architecture

```
Pragmatic.Migrations/
├── Schema/          # SchemaVersion, TableSchema, ColumnSchema, IndexSchema, ForeignKeySchema
├── Diff/            # SchemaDiffEngine, SchemaDiff
│   └── Changes/     # 15 SchemaChange subtypes (CreateTable, AddColumn, AlterPrimaryKey, etc.)
├── Introspection/   # ISchemaIntrospector (3 providers), IConnectionFactory
├── Sql/             # ISqlMigrationGenerator, SqlGeneratorBase (3 providers)
├── Runner/          # IMigrationRunner, MigrationRunner, MigrationResult
├── Tenant/          # ITenantMigrationOrchestrator, TenantMigrationOptions
└── Configuration/   # MigrationsBuilder, MigrationProviderFactory
```

## Status

**Functional** within 1.0.0-alpha — the declarative schema diff on SQLite, PostgreSQL and SQL Server, the
CLI, hooks, and multi-tenant migrations; every reference application migrates with it. See the
[roadmap](../docs/ROADMAP.md).

## Documentation

- [Concepts](docs/concepts.md) — declarative schema, diff engine, provider-specific idiomatic SQL
- [Getting Started](docs/getting-started.md) — fresh DB, schema evolution, idempotent rerun, multi-provider
- [Common Mistakes](docs/common-mistakes.md)
- [Troubleshooting](docs/troubleshooting.md)

Samples:

- [`Pragmatic.Migrations.Samples`](samples/Pragmatic.Migrations.Samples/README.md) — runnable scenarios: fresh database, schema evolution, idempotent rerun, data migrations, excluded tables, multi-provider SQL, hooks, leader election, concurrent indexes, tenant orchestration, CLI usage

## Requirements

- .NET 10.0+
- PostgreSQL, SQL Server or SQLite — there is no MySQL generator or introspector
- `Pragmatic.SourceGenerator` analyzer (emits the desired `SchemaVersion`)

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Migrations is licensed under the **PolyForm Small Business 1.0.0** license (free for small businesses; commercial license above the threshold).
