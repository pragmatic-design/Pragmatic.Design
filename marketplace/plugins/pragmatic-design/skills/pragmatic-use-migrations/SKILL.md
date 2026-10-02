---
name: pragmatic-use-migrations
description: Use when the schema must follow the entities, a change is breaking, or data needs a backfill — Pragmatic.Migrations, no migration files, data migrations, snapshots, DB-per-tenant, the pragmatic-migrate CLI.
---

# Pragmatic Use Migrations

**Covers:** Declarative schema migrations with Pragmatic.Migrations, no migration files — the runner diffs the schema generated from the entities against the database and applies it in one transaction, blocking data loss unless forced; data migrations, snapshots, DB-per-tenant, the pragmatic-migrate CLI.

`Pragmatic.Migrations` replaces EF Core Migrations. There are **no migration files** and no
design-time tools: the source generator emits the desired schema from your `[Entity]` types at
compile time, and at startup the runner introspects the live database, diffs it, and applies the
difference.

```
[Entity] classes ──SG──▶ {Db}Schema.Current ──▶ diff vs live DB ──▶ idempotent SQL ──▶ apply in one TX
```

## Packages

```xml
<PackageReference Include="Pragmatic.Migrations" Version="1.0.0-alpha.*" />
<!-- plus the driver, in the HOST project -->
<PackageReference Include="Npgsql" Version="9.*" />
```

The host must reference the database driver (`Npgsql` / `Microsoft.Data.SqlClient` /
`Microsoft.Data.Sqlite`), not only the module projects.

## Enable it

```csharp
await PragmaticApp.RunAsync(args, builder =>
{
    builder.UsePragmaticMigrations();
});
```

That is the whole setup. Every database in the topology is migrated at startup, before the app
serves traffic. **A failed migration aborts startup** — the host never runs on a stale schema.

## Configuration

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.OnlyDatabase<AppDatabase>();       // migrate just this database
    m.DryRun();                          // compute and report the plan, execute nothing
    m.Force();                           // allow breaking changes (see below)
    m.ExcludeTable("LegacyAudit");       // tables owned by someone else
    m.ManageDeclaredTablesOnly();        // never DROP a table this host did not declare
    m.UseAuditTable("_MyHistory");       // rename __PragmaticSchema
    m.UseConcurrentIndexes();            // build indexes after commit, without locking the table
    m.AddDataMigration<BackfillStatus>();
    m.UseProvider(MigrationConstants.ProviderPostgreSql, cs => new NpgsqlConnection(cs));
});
```

## Breaking changes are blocked

A change that can lose data or fail against existing rows is **breaking**, and the migration refuses
it unless you opt in with `Force()`:

- `DROP TABLE`, `DROP COLUMN`
- `SET NOT NULL` (fails if any row holds NULL)
- a primary-key change
- `ADD COLUMN` that is NOT NULL with no default
- a narrowing type change — smaller capacity in the same family (`varchar(256)`→`varchar(50)`), or a
  change of family altogether (`varchar(50)`→`int`)

The result carries `FailedChangeIndex`, `FailedChangeSql` and actionable `Suggestions`. The intended
workflow is: `DryRun()` → review → `Force()` for a controlled deploy. Never `Force()`
unconditionally in production.

## Data migrations — backfilling data

`IDataMigration` runs **exactly once per database**, tracked by name in `__PragmaticDataMigrations`,
in its own transaction after the schema phase. It runs even when the schema did not change.

```csharp
public sealed class BackfillStatus : IDataMigration
{
    public string Name => "2026-05_BackfillStatus";   // stable; renaming makes it run again
    public int Order => 0;
    public string? DatabaseName => null;              // null = every database

    public async Task MigrateAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct = default)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;                 // REQUIRED — atomic with the tracking record
        cmd.CommandText = """UPDATE "Orders" SET "Status" = 'pending' WHERE "Status" IS NULL""";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
```

Register with `m.AddDataMigration<BackfillStatus>()`.

Two neighbours, easy to confuse:

| Use | When |
|-----|------|
| `IDataMigration` | A one-time transformation. Runs once per database, after the schema. |
| `IMigrationSeedProvider` | Idempotent reference data (`ON CONFLICT DO NOTHING`). Runs after every migration that applied changes. |
| `IMigrationHook` | Logic tied to one specific change, inside the migration transaction (`BeforeChangeAsync` can skip it; `AfterChangeAsync` populates a column right after it is added). |

## Schema snapshot — reviewing schema changes in a PR

Commit a generated JSON snapshot so schema changes show up in code review and branch conflicts
become ordinary merge conflicts.

```xml
<PropertyGroup>
  <PragmaticSchemaSnapshot>true</PragmaticSchemaSnapshot>
</PropertyGroup>
```

CI gate:

```bash
pragmatic-migrate snapshot --assembly path/to/MyApp.dll --output schema
git diff --exit-code schema/
```

A non-empty diff means an entity changed without the snapshot being regenerated.

## CLI (optional)

```bash
dotnet tool install -g Pragmatic.Migrations.Cli

pragmatic-migrate status   --project src/MyApp.Host        # what would change
pragmatic-migrate script   --assembly bin/.../MyApp.dll    # print the SQL
pragmatic-migrate apply    --project src/MyApp.Host --verbose
pragmatic-migrate snapshot --project src/MyApp.Host --output schema
pragmatic-migrate history  --project src/MyApp.Host
```

Add `--audit-table <name>` when the host renamed the audit table with `UseAuditTable`: the CLI does
not build the host's DI container, so it has to be told.

`apply` shows the diff, prices the data impact of each breaking change and asks before applying it,
then delegates to the same runner the host uses. It applies **schema changes only** — data
migrations, hooks and seed providers live in the host's DI container.

Flags: `--assembly` / `--project` (builds it), `--connection`, `--config`, `--database`,
`--dry-run`, `--force`, `--yes` (non-interactive), `--json`, `--agent`, `--verbose`, `--no-color`.

## Multiple instances

Only one instance migrates. `DatabaseLeaderElection` claims a row in `__PragmaticLock`; the others
wait, then **verify the schema** before reporting success — a leader that crashed without migrating
does not let the followers boot on a stale database. If leader election itself fails, an instance
defaults to **not leader** (never assumes leadership: that would be split-brain). No extra
infrastructure. SQLite skips it entirely.

## DB-per-tenant

With `services.AddDbPerTenant(...)`, every active tenant holding a dedicated connection string is
migrated at startup, right after the host's own database. A tenant failure aborts startup. Tune with
`TenantMigrationOptions`: `MaxParallelism` (1), `ContinueOnFailure` (false), `SuspendOnFailure`
(true), `TenantTimeout` (10 min).

## Sharing a database with another system

By default a table present in the database but absent from your entities is proposed for `DROP` — a
breaking change, so startup blocks rather than deleting anything. Either name the foreign tables
(`m.ExcludeTable("LegacyAudit")`) or opt out wholesale (`m.ManageDeclaredTablesOnly()`). The
trade-off of the latter: renaming an entity leaves the old table behind.

## Gotchas

- **`SchemaVersion.Hash` is derived, not supplied** — and it IS comparable across sides: both the
  generated schema and an introspected one are hashed by the same canonical function, which
  normalises types and defaults the way the diff does. `current.Hash == MyDbSchema.Current.Hash`
  means the database is up to date; that is what the runner's fast path and
  `pragmatic-migrate status` rely on.
- **The audit table is a record, not an input.** What gets applied comes from diffing the entities
  against the live database. Losing `__PragmaticSchema` costs history, not correctness — but losing
  `__PragmaticDataMigrations` makes every `IDataMigration` run again.
- **A rename needs a hint.** Put `[RenamedFrom("FirstName")]` (`Pragmatic.Persistence.Entity`) on the
  renamed property and the migration emits `RENAME COLUMN`; without it a rename is a drop plus an add
  — and the drop is breaking. The attribute is for properties only: renaming an **entity** is a new
  table and a dropped one.
- **No down migrations.** Rolling back means deploying the previous model.
- **SQLite rebuilds tables.** Nullability, default, type and FK changes are applied by recreating the
  table (rename → recreate → copy → drop), inside the migration transaction. Handled for you; just
  do not call `GenerateChangeScript` yourself for those changes — it throws, by design, rather than
  returning SQL that looks applied but is not.
- **PostgreSQL introspects every non-system schema.** In a multi-schema database, expect tables you
  did not declare to show up in the diff.

## Related

- `pragmatic-use-persistence` — the `[Entity]` model the desired schema is generated from
- `pragmatic-use-multitenancy` — DB-per-tenant setup
- Module docs: `Pragmatic.Migrations/docs/` (concepts, getting-started, common-mistakes, troubleshooting)

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Warehouse and Showcase example
applications — code that compiles and that `Warehouse.IntegrationTests` and `Showcase.IntegrationTests`
exercise — and kept identical to it by the gate: a service's host migrating at start, its database
marker, the schema snapshot and its project setting, and the runner refusing a breaking change, running a
data migration once and leaving a foreign table alone.

`DryRun()`/`Force()` in a host, `IMigrationSeedProvider`, `IMigrationHook` and the CLI are used by no
tested application yet, so there is no example of them here: the sections above are the reference.
