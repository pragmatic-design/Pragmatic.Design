---
title: "Getting Started with Pragmatic.Migrations"
description: "Step-by-step guide to adding declarative schema migrations to your Pragmatic host."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Migrations/docs/getting-started.md
sidebar:
  order: 2
---
Step-by-step guide to adding declarative schema migrations to your Pragmatic host.

---

## Prerequisites

- A Pragmatic host with at least one `[Entity]` declared
- A database connection string in `appsettings.json`
- One of the supported database providers referenced in your host `.csproj`:
  - `Npgsql` (PostgreSQL)
  - `Microsoft.Data.SqlClient` (SQL Server)
  - `Microsoft.Data.Sqlite` (SQLite)

---

## Step 1: Add the NuGet Package

```bash
dotnet add package Pragmatic.Migrations
```

That is the only package you need. The Source Generator in `Pragmatic.SourceGenerator` automatically detects `Pragmatic.Migrations` in your references and generates the `SchemaVersion` metadata at compile time.

---

## Step 2: Enable Migrations in Program.cs

### Zero-Config (Recommended)

```csharp
await PragmaticApp.RunAsync(args, builder =>
{
    builder.UsePragmaticMigrations();
});
```

This migrates **all** databases detected in your topology. The SG reads your `[Entity]` attributes, computes the desired schema, and the runner diffs it against the live database at startup.

### With Configuration

```csharp
await PragmaticApp.RunAsync(args, builder =>
{
    builder.UsePragmaticMigrations(m =>
    {
        m.OnlyDatabase<ShowcaseAppDatabase>();  // Migrate only this DB
    });
});
```

---

## Step 3: Verify Your Entities

The SG reads every class decorated with `[Entity]` and produces a `SchemaVersion` record containing table definitions, columns, indexes, and foreign keys. For example:

```csharp
// In Showcase.Booking module
[Entity]
public partial class Reservation
{
    public string GuestName { get; set; } = "";
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public decimal TotalAmount { get; set; }
}
```

At compile time, the SG emits a `SchemaVersion` with a `TableSchema` for `Reservations` (pluralized), including columns, types, nullability, and a content hash.

---

## Step 4: Run the Host

```bash
dotnet run
```

On startup, the migration runner executes this pipeline:

```
1. Leader election     → Only one instance migrates; the others wait, then verify the schema
2. Advisory lock       → Cross-process guard held for the whole run
3. Introspect DB       → Read current schema via information_schema / sys.* / PRAGMA
4. Compute diff        → Compare desired SchemaVersion vs current
5. Execute in TX       → Each change in its own command, all in one transaction
6. Record audit        → Write to __PragmaticSchema
7. Run seed providers  → Optional post-migration data seeding
8. Run data migrations → IDataMigration, once per database, in its own transaction
```

You will see structured log output:

```
info: Pragmatic.Migrations.Runner.MigrationRunner
      [default] Starting migration (PostgreSql) — desired hash: a3f7b2c1
info: Pragmatic.Migrations.Runner.MigrationRunner
      [default] Diff computed: 3 changes (0 breaking)
info: Pragmatic.Migrations.Runner.MigrationRunner
      [default] Migration complete: 3 changes applied in 47ms
```

Per-change lines (`Applying 1/3: CREATE TABLE "Reservations"`) are logged at `Debug`; see
[Troubleshooting](/modules/migrations/troubleshooting/) for the log-level snippet.

---

## Step 5: Inspect with Dry Run

Before applying to production, use `DryRun` to see what SQL would be generated without executing:

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.DryRun();
});
```

The `MigrationResult` contains the full SQL script in `GeneratedSql` and the list of `SchemaChange` objects in `AppliedChanges`. In dry-run mode, `ChangesApplied` is always `0` (nothing was executed).

The progress stream reports each change with a `[safe]` or `[BREAKING]` indicator:

```
[default] Dry run: 3 changes
  [safe] CREATE TABLE "Reservations"
  [safe] CREATE INDEX "IX_Reservations_GuestName"
  [safe] ADD FOREIGN KEY "Reservations" → "Guests"
```

---

## Step 6: Handle Breaking Changes

By default, breaking changes (DROP TABLE, DROP COLUMN, narrowing type changes) are **blocked**. The runner returns a failed `MigrationResult` with suggestions:

```
Blocked: 2 breaking changes require Force=true
```

To apply them explicitly:

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.Force();
});
```

In production, the recommended workflow is:

1. Run with `DryRun()` first to inspect the SQL
2. Review breaking changes and back up data if needed
3. Apply with `Force()` in a maintenance window

---

## Step 7: Filter by Database

In multi-database topologies (e.g., separate databases for Booking and Billing), you can target a specific database:

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.OnlyDatabase<ShowcaseAppDatabase>();
});
```

The filter matches against `SchemaVersion.DatabaseName`, which is set by the SG from your `[Include<TModule, TDatabase>]` topology attributes.

---

## Step 8: Custom Audit Table

Every migration is recorded in `__PragmaticSchema` by default. You can change the table name:

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.UseAuditTable("_MyMigrationHistory");
});
```

Both the table and the index are created under the name you choose, and the original
`__PragmaticSchema` (if the host ever ran with the default) stays excluded from introspection, so it
is never proposed for deletion.

The audit table stores:

| Column | Description |
|--------|-------------|
| `Hash` | Schema version marker of the desired schema |
| `SchemaJson` | The full desired schema, serialized |
| `SqlScript` | The SQL that was executed |
| `ChangeCount` | Number of changes |
| `DurationMs` | Execution time |
| `AppliedAt` | Timestamp |
| `AppliedBy` | Machine name |

---

## Step 9: Seed Data After Migration

Implement `IMigrationSeedProvider` to run data seeding after migrations complete:

```csharp
public sealed class DefaultRolesSeedProvider : IMigrationSeedProvider
{
    public string? DatabaseName => null;  // null = all databases
    public int Order => 100;

    public async Task SeedAsync(DbConnection connection, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO "Roles" ("Id", "Name")
            VALUES ('admin', 'Administrator')
            ON CONFLICT DO NOTHING
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
```

Register it in DI and the runner will invoke it after successful migration.

### Data Migrations vs Seeding

`IMigrationSeedProvider` runs after **every** successful migration: use it for
idempotent reference data (`ON CONFLICT DO NOTHING`). For a **one-time** data
transformation (backfilling a new column, converting values, moving data between
tables), implement `IDataMigration` instead: it runs exactly once per database, tracked
by name in `__PragmaticDataMigrations`, inside its own transaction.

```csharp
public sealed class BackfillOrderStatus : IDataMigration
{
    public string Name => "2026-05_BackfillOrderStatus";

    public async Task MigrateAsync(DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;  // required: atomic with the tracking record
        cmd.CommandText = "UPDATE \"Orders\" SET \"Status\" = 'pending' WHERE \"Status\" IS NULL";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
```

Register it with `builder.UsePragmaticMigrations(m => m.AddDataMigration<BackfillOrderStatus>())`.

---

## Step 10: Multi-Instance Deployment

When running multiple instances of the same host (e.g., behind a load balancer), only one instance should run migrations. This is handled automatically via `DatabaseLeaderElection`:

1. The first instance to claim the lock in `__PragmaticLock` becomes the leader
2. Other instances poll every 2 seconds until the leader finishes
3. After migration completes, the leader releases the lock
4. If the leader crashes, the lock expires after the configured timeout (default: 5 minutes)

No additional configuration is needed -- leader election uses the same database connection as the migration itself.

---

## Step 11: Sharing the Database With Another System

By default a table that exists in the database but not in your entities is proposed for `DROP`, a
breaking change, so startup is blocked rather than data deleted. If another application (or a DBA)
owns tables in the same database, tell the runner:

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.ExcludeTable("LegacyAudit", "spatial_ref_sys");   // name them explicitly, or…
    m.ManageDeclaredTablesOnly();                        // …never touch what you did not declare
});
```

With `ManageDeclaredTablesOnly()` the host manages the tables it declares and leaves everything else
alone. The trade-off: renaming an entity looks like "new table added, old table unknown", so the old
table survives and has to be dropped by hand.

---

## Step 12: The CLI (optional)

The host migrates itself at startup; nothing else is required. When you want to inspect or apply
migrations outside the host (a DBA review, a deploy step, a CI gate), install the tool:

```bash
dotnet tool install -g Pragmatic.Migrations.Cli
```

```bash
pragmatic-migrate status   --project src/MyApp.Host        # what would change
pragmatic-migrate script   --assembly bin/.../MyApp.dll    # print the SQL
pragmatic-migrate apply    --project src/MyApp.Host --verbose
pragmatic-migrate snapshot --project src/MyApp.Host --output schema
pragmatic-migrate history  --project src/MyApp.Host
```

`--project` builds the project and uses its output; `--assembly` points at a built `.dll` directly.
`apply` shows the diff, prices the data impact of each breaking change and asks before applying it,
then hands execution to the same runner the host uses. Only schema changes are applied: data
migrations, hooks and seed providers live in the host's DI container and run when the host migrates.

---

## Complete Example

```csharp
// Program.cs in Showcase.Host
await PragmaticApp.RunAsync(args, builder =>
{
    builder.UsePragmaticMigrations(m =>
    {
        m.OnlyDatabase<ShowcaseAppDatabase>();
    });

    // Optional: enable the Pragmatic Agent for distributed coordination.
    // Migration leader election does NOT need it: DatabaseLeaderElection
    // (the __PragmaticLock table) already serializes migrations across instances.
    builder.UseAgent();
});
```

```json
// appsettings.json
{
  "ConnectionStrings": {
    "App": "Host=localhost;Database=showcase;Username=postgres;Password=postgres"
  }
}
```

---

## What Happens Under the Hood

```
Compile Time:
  [Entity] Reservation → SG → SchemaVersion (hash: a3f7b2c1)
                                       ├── TableSchema: "Reservations"
                                       │   ├── ColumnSchema: "Id" (uuid, PK)
                                       │   ├── ColumnSchema: "GuestName" (text)
                                       │   ├── ColumnSchema: "CheckIn" (date)
                                       │   └── ...
                                       ├── IndexSchema: "IX_Reservations_GuestName"
                                       └── ForeignKeySchema: → "Guests"

Runtime:
  MigrationRunner.MigrateAsync(context)
    → DatabaseLeaderElection.TryBecomeLeaderAsync()   // claim __PragmaticLock
    → PostgreSqlSchemaIntrospector.IntrospectAsync()   // read information_schema
    → SchemaDiffEngine.ComputeDiff(desired, current)   // produce SchemaChange[]
    → PostgreSqlMigrationGenerator.GenerateScript()    // emit idempotent SQL
    → Execute each change in a transaction              // per-change commands
    → SchemaAuditStore.RecordMigrationAsync()          // write __PragmaticSchema
    → IMigrationSeedProvider.SeedAsync()               // optional data seeding
    → DatabaseLeaderElection.ReleaseLeadershipAsync()  // release lock
```

---

## Next Steps

- [Concepts](/modules/migrations/concepts/) -- Architecture deep dive: diff engine, SQL generation, leader election
- [Common Mistakes](/modules/migrations/common-mistakes/) -- 8 pitfalls and how to avoid them
- [Troubleshooting](/modules/migrations/troubleshooting/) -- FAQ and error recovery
