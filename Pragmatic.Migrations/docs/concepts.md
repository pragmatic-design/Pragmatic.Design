# Concepts — Pragmatic.Migrations Architecture

Deep dive into how declarative schema migrations work: from compile-time metadata to runtime execution.

---

## Overview

Pragmatic.Migrations eliminates migration files entirely. Instead of an imperative sequence of "add this column, drop that table", you declare the desired state via `[Entity]` attributes and the framework computes what needs to change.

```
┌──────────────────────┐     ┌──────────────────────────────────────────────────┐
│   Compile Time       │     │   Runtime                                        │
│                      │     │                                                  │
│   Source Generator   │     │   Introspect DB                                  │
│   reads [Entity]     │────▶│   ↓                                              │
│   ↓                  │     │   SchemaDiffEngine.ComputeDiff(desired, current) │
│   SchemaVersion      │     │   ↓                                              │
│   (desired schema)   │     │   ISqlMigrationGenerator.GenerateChangeScript()  │
│                      │     │   ↓                                              │
│                      │     │   MigrationRunner.MigrateAsync(context)          │
└──────────────────────┘     └──────────────────────────────────────────────────┘
```

---

## 1. Schema Metadata (Compile Time)

### SchemaVersion

The Source Generator produces a `SchemaVersion` record for each database in your topology:

```csharp
public sealed record SchemaVersion(
    ImmutableArray<TableSchema> Tables,
    string? DatabaseName = null,          // e.g. "ShowcaseAppDatabase"
    string? ProviderName = null,          // "PostgreSql" | "SqlServer" | "Sqlite"
    string? ConfigKey = null)
{
    public string Hash { get; }           // derived — see below
}
```

Each `TableSchema` contains:

```csharp
public sealed record TableSchema(
    string Name,
    string? SchemaName,                   // "public" for PG, "dbo" for SQL Server, null for SQLite
    ImmutableArray<ColumnSchema> Columns,
    ImmutableArray<IndexSchema> Indexes,
    ImmutableArray<ForeignKeySchema> ForeignKeys);
```

And each column:

```csharp
public sealed record ColumnSchema(
    string Name,
    string SqlType,                       // provider-specific: "uuid", "nvarchar(256)", "TEXT"
    bool IsNullable,
    bool IsPrimaryKey,
    string? DefaultValue = null,          // SQL expression: "false", "now()"
    string? RenamedFrom = null);          // lets the diff see a rename instead of drop+add
```

### About the hash

`Hash` is the **canonical identity** of a schema: two schemas the diff engine would call identical
have the same hash, whichever side produced them. It is derived, never supplied — both the
compile-time schema and every introspected one go through `SchemaHasher`, which applies exactly the
normalisation the diff applies before comparing.

That matters because the two sides describe the same database in different vocabularies: the
generator writes `integer` where PostgreSQL reports `int4`, `boolean` where it reports `bool`, and
SQL Server stores a default of `0` as `((0))`. Hashing each side's own text would give identical
schemas different hashes, so the two could not be compared at all.

The digest covers precisely what the diff compares: table and column names (case-folded), normalised
types, nullability, normalised defaults, the ordered primary key, and indexes and foreign keys by
structure rather than by name. Everything else is excluded — the schema qualifier (the generator
usually omits it), declaration order, and the database/provider/config-key metadata.

Three things follow:

- **Equal hashes mean no changes.** The runner uses this as a fast path: introspect, compare, and
  skip the diff entirely when they match.
- **The audit trail becomes meaningful.** `__PragmaticSchema.Hash` records which schema version a
  database is actually at, directly comparable with `MyDbSchema.Current.Hash`.
- **The committed snapshot detects drift** by hash alone, without touching a database.

---

## 2. Schema Introspection (Runtime)

### ISchemaIntrospector

Each provider implements `ISchemaIntrospector` to read the current database schema:

| Provider | Strategy | System catalogs | Round-trips |
|----------|----------|-----------------|-------------|
| PostgreSQL | `information_schema` + `pg_catalog` | `information_schema.tables/columns`, `pg_index` | 4 |
| SQL Server | `sys.*` views | `sys.tables`, `sys.columns`, `sys.indexes`, `sys.foreign_keys` | 4 |
| SQLite | `sqlite_master` + the `pragma_*` table-valued functions | `pragma_table_info` / `index_list` / `index_info` / `foreign_key_list`, joined against `sqlite_master`, plus `sqlite_master.sql` for partial-index predicates | 5 |

The round-trip count is **fixed**, not per table: one query for the tables, one for every column,
one for every index, one for every foreign key, and the results are grouped in memory. A per-table
shape would make host startup slower with every entity added — thirty tables meant ninety-one
queries before this. On SQLite the same batching needs the `pragma_*` table-valued functions
(SQLite 3.16+), because a bare `PRAGMA table_info(x)` can only ever describe one table.

Framework-internal tables are always excluded: `__PragmaticSchema` (audit — plus your custom name if
you set one), `__PragmaticDataMigrations`, `__PragmaticLock`, `__EFMigrationsHistory`.

### Provider auto-detection

You never specify the provider manually. The SG reads which EF Core provider package is referenced
and sets `SchemaVersion.ProviderName`. At runtime `MigrationProviderFactory` resolves the matching
introspector, SQL generator and connection factory through keyed DI services.

### Tables this host did not declare

By default any table found in the database but absent from the desired schema becomes a `DROP TABLE`
— a breaking change, so it blocks startup instead of deleting anything. On a database shared with
another system that is the wrong default; two ways out:

- `ExcludeTable("LegacyAudit", "spatial_ref_sys")` — name the foreign tables explicitly.
- `ManageDeclaredTablesOnly()` — never propose to drop a table this host does not declare. The
  trade-off: renaming an entity leaves the old table behind, to be dropped by hand.

---

## 3. Diff Engine

### SchemaDiffEngine

`ISchemaDiffEngine` compares two `SchemaVersion` records and produces a `SchemaDiff`:

```csharp
SchemaDiff ComputeDiff(SchemaVersion desired, SchemaVersion? current, bool dropUnknownTables = true);
```

### Change types

| Change type | Description | Breaking? |
|-------------|-------------|-----------|
| `CreateTable` | New table with all columns, PK, and (SQLite) inline FKs | No |
| `DropTable` | Table removed from desired schema | **Yes** |
| `AddColumn` | New column on existing table | Yes when NOT NULL with no default |
| `DropColumn` | Column removed from desired schema | **Yes** |
| `RenameColumn` | Detected via `ColumnSchema.RenamedFrom` | No |
| `AlterColumnType` | Column type changed | Yes when narrowing (below) |
| `AlterColumnNullability` | NULL ⇄ NOT NULL | Yes when setting NOT NULL |
| `AlterColumnDefault` | Default value changed or removed | No |
| `AlterPrimaryKey` | The set (or order) of primary-key columns changed | **Yes** |
| `AddIndex` | New index | No |
| `DropIndex` | Index removed | No |
| `AddForeignKey` | New FK constraint | No |
| `DropForeignKey` | FK removed | No |

### Ordering

Changes are emitted so each one can succeed when its turn comes — **drops first**, then creates,
then alterations:

1. `DropForeignKey` — release the constraints that would block everything else
2. `DropIndex`
3. `DropColumn`
4. `DropTable`
5. `CreateTable`
6. `AddColumn`, `AlterColumnType`, `AlterColumnNullability`, `AlterColumnDefault`, `RenameColumn`
7. `AlterPrimaryKey` — once the columns exist and are typed
8. `AddIndex`
9. `AddForeignKey` — last, so both tables and the key it points at already exist

### Breaking change detection

A change is `IsBreaking` when it can lose data or fail against existing rows:

- `DropTable`, `DropColumn` — the data is gone
- `AlterColumnNullability` to NOT NULL — fails if any row holds NULL
- `AlterPrimaryKey` — drops a uniqueness guarantee, and fails if the rows do not satisfy the new key
- `AddColumn` that is NOT NULL with no default — existing rows cannot satisfy it
- `AlterColumnType` when narrowing. Narrowing means **either** less capacity within the same family
  (`varchar(256)` → `varchar(50)`, `decimal(18,2)` → `decimal(18,1)`, `text` → `varchar(N)`) **or**
  a change of family altogether (`varchar(50)` → `int`, `timestamptz` → `text`), where the
  conversion either fails or reinterprets every stored value.

Breaking changes are **blocked by default**: the runner returns a failed `MigrationResult` carrying
suggestions, and `Force()` opts in.

At host startup a failed `MigrationResult` aborts startup (fail-fast) — the host never runs on a
stale or partial schema.

---

## 4. SQL Generation

Each provider generates idempotent SQL. Because the runner executes one change at a time,
`GenerateChangeScript(change)` is the primary entry point; `GenerateScript(diff)` renders the whole
plan for dry runs, the `script` command, and the audit record.

**PostgreSQL** — `CREATE TABLE IF NOT EXISTS` plus `DO $$` blocks where a plain `IF NOT EXISTS`
cannot express the check:

```sql
CREATE TABLE IF NOT EXISTS "public"."Reservations" (
    "Id" uuid NOT NULL,
    "GuestName" text NOT NULL,
    CONSTRAINT "PK_Reservations" PRIMARY KEY ("Id")
);
```

**SQL Server** — `IF NOT EXISTS (sys.*)` guards. Two provider quirks the generator handles for you:
`ALTER COLUMN` restates the entire column definition (so a type change always restates NULL/NOT NULL
— omitting it makes SQL Server quietly turn the column nullable), and a default cannot be replaced
in place (so the existing constraint is looked up by name and dropped before the new one is added).

**SQLite** — no `ALTER COLUMN`, and no way to add or drop a foreign key on an existing table. Those
changes are applied by rebuilding the table:

```sql
PRAGMA defer_foreign_keys = ON;      -- transaction-scoped; PRAGMA foreign_keys is a no-op in a tx
ALTER TABLE "Orders" RENAME TO "__Orders_rebuild";
CREATE TABLE "Orders" ( ... desired shape, PK and FKs inline ... );
INSERT INTO "Orders" ("Id", "Note") SELECT "Id", "Note" FROM "__Orders_rebuild";
DROP TABLE "__Orders_rebuild";
CREATE INDEX IF NOT EXISTS "IX_Orders_Note" ON "Orders" ("Note");
```

The runner asks `GetRebuildTableName(change)` first: when a provider answers with a table name, one
rebuild is emitted for that table and every other pending change on it is covered by the same
statement. Only columns present on **both** sides are copied, so a column added in the same
migration does not break the `SELECT`. A table created by the same migration is never rebuilt — its
`CREATE TABLE` already carries the target shape.

Asking a provider to render a rebuild-only change on its own throws. It never returns SQL that looks
applied but is not.

### Concurrent index builds

`UseConcurrentIndexes()` moves every `AddIndex` to a phase that runs **after** the schema transaction
commits, out of transaction — PostgreSQL `CREATE INDEX CONCURRENTLY`, SQL Server
`WITH (ONLINE = ON)` (Enterprise), SQLite ignores the flag. A concurrent build that fails leaves the
index INVALID on the server (the schema is already committed) and the run is reported as failed.

---

## 5. Migration Runner

### Pipeline

`MigrationRunner.MigrateAsync(context)`:

```
Phase 0: Leader election → non-leaders wait, then VERIFY the schema (§6)
Phase 1: Open connection (with retry) + acquire the advisory lock
Phase 2: Introspect current schema
         → hashes equal: skip ahead to the data-migration phase
Phase 3: Compute diff
         → no changes: skip ahead to the data-migration phase
Phase 4: Render the plan; a dry run returns here; breaking changes blocked unless Force
Phase 5: Execute every change inside ONE transaction (rebuilds consolidated per table)
         → on failure: rollback + failed-change index, its SQL, and suggestions
Phase 6: Post-commit concurrent index builds (only with UseConcurrentIndexes)
Phase 7: Record the run in the audit table
Phase 8: Seed providers (IMigrationSeedProvider) — only when changes were applied
Phase 9: Data migrations (IDataMigration), own transaction — also when the diff was empty
```

### Migration hooks

`IMigrationHook` runs around individual changes, inside the migration transaction.
`BeforeChangeAsync` returns false to skip the change; `AfterChangeAsync` runs once it succeeded.
Create commands from `MigrationStepContext.Connection` and set their `Transaction` to
`MigrationStepContext.Transaction`, so the data change is atomic with the schema change.

### Data migrations

`IDataMigration` is a named, versioned data transformation. Each runs **exactly once per database**,
tracked by `Name` in `__PragmaticDataMigrations`, in a dedicated transaction after the schema phase.
They run even when the schema diff is empty, so a data-only change still applies. If one throws, its
transaction is rolled back and the whole migration result is failed.

### Transaction safety

All schema changes execute inside a single transaction. On failure: rollback, and the
`MigrationResult` carries `FailedChangeIndex`, `FailedChangeSql` and context-aware `Suggestions`.
The one exception is the post-commit concurrent-index phase, which by definition runs after commit.

### Advisory locking

Before introspection the runner takes a database advisory lock — PostgreSQL `pg_advisory_lock`,
SQL Server `sp_getapplock` with `@LockOwner = 'Session'` (session-scoped, because the lock is taken
before the transaction begins and released after it commits), SQLite no-op (single writer). It is
held for the whole run and works **across processes**; it is separate from leader election.

### Progress streaming

The runner reports through `IMigrationProgressStream`: `Phase` ("analyzing", "applying", "complete",
"error", "waiting", "migration"), `Message`, `ProgressPercent`, `DatabaseName`, and
`IsError` / `ErrorDetail`.

---

## 6. Leader Election

### The problem

When several instances start at once (a Kubernetes rolling deploy), only one should migrate.

### DatabaseLeaderElection

The default strategy coordinates through the database itself — no Redis, Consul or etcd. It keeps a
single row in `__PragmaticLock`:

| Column | Description |
|--------|-------------|
| `LockName` | Always `'migration'` |
| `HolderId` | Guid7-based host identifier |
| `AcquiredAt` | When the lock was acquired |
| `ExpiresAt` | Lease expiry — taken from `MigrationOptions.Timeout`, so it always outlives the migration it protects |

Acquisition is one atomic UPDATE that succeeds in exactly three cases: no holder, the same holder
(re-entrant), or an expired lease (the previous leader crashed).

### FallbackLeaderElection

Wraps the election **fail-safe**: when the inner election throws (database unreachable, missing
permissions) this instance reports **not leader** and waits. It deliberately does *not* assume
leadership — doing so on a transient error would let every pod migrate at once (split-brain). The
log line to look for is `Leader election failed — defaulting to NOT leader (fail-safe)`.

### AlwaysLeaderElection

Used when no connection factory is available, and for SQLite: a single-file / single-process
database has nothing to coordinate, and its in-memory variant gives each connection a private
database, so a lock table written by one connection is invisible to the next.

### Follower behaviour

Non-leaders poll `__PragmaticLock` (interval `MigrationOptions.LeaderPollInterval` plus jitter,
giving up after 5 consecutive polling failures). When the lock clears the follower **verifies the
schema** before declaring success: the leader releases its lock whether it succeeded or failed, so a
free lock is not evidence that the database is ready. If the schema is still behind, the follower
fails — and the host aborts instead of serving traffic against a half-migrated database.

---

## 7. Tenant Migration Orchestration

For DB-per-tenant deployments, `ITenantMigrationOrchestrator` applies the same desired schema to
every active tenant that has a dedicated connection string. `AddDbPerTenant(...)` registers it, and
the generated host startup invokes it right after the host's own database has been migrated. A
tenant failure aborts startup, exactly like a failure on the main database.

Per tenant: mark `Migrating` → run the migration → mark `Active` on success, `Suspended` on failure.
Progress is reported through `IHostStatus` and `IControlPlane`.

`TenantMigrationOptions` tunes the sweep:

| Option | Default | Effect |
|--------|---------|--------|
| `MaxParallelism` | 1 | Tenants migrated at the same time. Values below 1 are clamped to 1. |
| `ContinueOnFailure` | false | Stop at the first failure; untouched tenants are reported as *skipped*. Only meaningful when `MaxParallelism` is 1 — a parallel sweep always attempts every tenant. |
| `SuspendOnFailure` | true | Suspend a failed tenant. Set false to leave it `Migrating` so a retry can pick it up without an operator un-suspending it. |
| `TenantTimeout` | 10 min | Bounds one tenant, so a single unreachable database cannot stall the sweep. Exceeding it fails that tenant only. |

Register your own instance before `AddDbPerTenant` to override the defaults.

---

## 8. Observability

### Structured logging

All log messages are `[LoggerMessage]` source-generated methods in `MigrationDiagnostics`:

| Event | Level |
|-------|-------|
| Migration started / schema up to date / diff computed / complete / dry run / skipped | Information |
| Applying change, change applied | Debug |
| Migration failed, follower schema stale | Error |
| Breaking changes blocked, rollback failed | Warning |

### OpenTelemetry

ActivitySource `Pragmatic.Migrations`, one span per run, tagged `db.name`, `db.provider`,
`migrations.change_count`, `migrations.breaking_count`, `migrations.duration_ms`.

### Metrics

Meter `Pragmatic.Migrations`. Every instrument is tagged `db.name` and `db.provider` and is emitted
once per migration run — including a run that failed by throwing:

| Metric | Type | Description |
|--------|------|-------------|
| `pragmatic.migrations.executed` | Counter | Migration runs |
| `pragmatic.migrations.failed` | Counter | Runs that failed |
| `pragmatic.migrations.changes_applied` | Counter | Schema changes applied |
| `pragmatic.migrations.duration` | Histogram | Run duration (ms) |

---

## 9. Comparison with EF Core Migrations

| Aspect | EF Core Migrations | Pragmatic.Migrations |
|--------|-------------------|---------------------|
| **Approach** | Imperative (migration files) | Declarative (desired state) |
| **Migration files** | Required, one per change | None |
| **Design-time tools** | `dotnet ef` required | Not needed |
| **ModelSnapshot** | Required for diffing | Not needed (SG-generated) |
| **Idempotency** | Optional (`--idempotent` flag) | Always idempotent |
| **Multi-database** | Manual configuration | Auto-detected from topology |
| **Transaction safety** | Provider-dependent | Always (single transaction) |
| **Error reporting** | Generic exception | Per-change with suggestions |
| **Breaking detection** | None | Built-in, blocks by default |
| **Leader election** | Not included | Built-in (database-based) |
| **Tenant orchestration** | Manual | Built-in, run at startup |
| **Audit trail** | Not included | `__PragmaticSchema` table |
| **Data migrations** | Hand-written SQL in migration files | First-class `IDataMigration` — named, tracked, run-once |
| **Schema snapshot** | `ModelSnapshot.cs` (diff input, drifts) | Committed `schema/*.json` (review artifact, generated) |

### What it deliberately does not do

- **No down migrations.** The desired state is the model; rolling back means deploying the previous model.
- **No hand-written schema steps in the sequence.** Data transformations belong in `IDataMigration` (after the schema) or `IMigrationHook` (around one specific change).
- **Renames need a hint.** A rename is only recognised when the SG emits `ColumnSchema.RenamedFrom`; without it, it is a drop plus an add — and the drop is breaking.

---

## Architecture Diagram

```
Pragmatic.Migrations/
├── Schema/              # Data model for desired/current schema
│   ├── SchemaVersion    # Top-level: hash + tables + provider + config key
│   ├── TableSchema      # Name + schema + columns + indexes + FKs
│   ├── ColumnSchema     # Name, SQL type, nullable, PK, default, renamed-from
│   ├── IndexSchema      # Name, columns, unique flag, filter
│   └── ForeignKeySchema # Name, column, referenced table/column, ON DELETE
├── Diff/                # Comparison engine
│   ├── SchemaDiffEngine # Compares desired vs current
│   ├── SchemaDiff       # Result: ordered SchemaChange[]
│   └── Changes/         # 13 SchemaChange subtypes + SchemaChangeTarget
├── Introspection/       # Database schema readers + SchemaAuditStore
├── Sql/                 # Provider SQL generators (+ table rebuild for SQLite)
├── Runner/              # Execution engine
│   ├── MigrationRunner          # Pipeline (partials: Planning, Execution, LeaderElection)
│   ├── MigrationResult          # Success/failure + failed change + suggestions
│   ├── MigrationOptions         # DryRun, Force, Timeout, filters, audit table, …
│   ├── MigrationLock            # Advisory lock (cross-process)
│   ├── MigrationResilience      # Retry on transient connection failures
│   ├── DatabaseLeaderElection   # Cross-instance via __PragmaticLock
│   ├── FallbackLeaderElection   # Fail-safe wrapper (never assumes leadership)
│   └── MigrationDiagnostics     # Logging + OTel spans + metrics
├── Tenant/              # DB-per-tenant orchestration
├── Configuration/       # MigrationsBuilder, MigrationProviderFactory
└── Extensions/          # IPragmaticBuilder extensions
```
