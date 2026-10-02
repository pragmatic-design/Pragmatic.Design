---
title: "Common Mistakes — Pragmatic.Migrations"
description: "Nine pitfalls that developers hit when using declarative schema migrations, and how to avoid each one."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Migrations/docs/common-mistakes.md
sidebar:
  order: 3
---
Nine pitfalls that developers hit when using declarative schema migrations, and how to avoid each one.

---

## 1. Forgetting UsePragmaticMigrations()

**Symptom**: Entities compile fine, the host starts, but the database has no tables. No migration logs appear.

**Cause**: The SG generates `SchemaVersion` metadata at compile time, but the migration runner only activates when you call `UsePragmaticMigrations()` in `Program.cs`.

```csharp
// Wrong: no migration call
await PragmaticApp.RunAsync(args, builder =>
{
    // Missing: builder.UsePragmaticMigrations();
});

// Correct
await PragmaticApp.RunAsync(args, builder =>
{
    builder.UsePragmaticMigrations();
});
```

**Why it happens**: Unlike EF Core which has `Database.EnsureCreated()` or `Database.Migrate()` as explicit calls, Pragmatic.Migrations relies on the `IPragmaticBuilder` extension. Without it, the runner and its DI services are never registered.

---

## 2. Wrong Database Provider Package

**Symptom**: `InvalidOperationException: No IConnectionFactory registered for provider 'PostgreSql'. Ensure the provider's NuGet package is referenced and registered (e.g. via UseProvider)…`

**Cause**: A provider is only wired up when its driver is actually loadable. If the provider package is not referenced in the host project, that provider is not registered at all — and resolving it fails with the message above, naming the provider it could not find.

```xml
<!-- Wrong: provider only in module project, not in host -->
<ProjectReference Include="Showcase.Booking" />
<!-- Missing: Npgsql or Microsoft.Data.SqlClient in the host -->

<!-- Correct: host references the provider -->
<PackageReference Include="Npgsql" Version="9.*" />
```

**Rule**: The **host** project must reference the database provider NuGet package, not just the module projects. The SG detects the provider from your EF Core reference and sets `SchemaVersion.ProviderName`, but the runtime needs the actual connection type.

To skip the driver-probing entirely — and the reflection it uses — hand the runner a factory:

```csharp
builder.UsePragmaticMigrations(m =>
    m.UseProvider(MigrationConstants.ProviderPostgreSql, cs => new NpgsqlConnection(cs)));
```

---

## 3. Applying Breaking Changes Without Force

**Symptom**: Migration returns `Success = false` with message: `Breaking changes detected (2). Use Force=true to apply.`

**Cause**: By default, the runner blocks any change marked `IsBreaking = true`. This includes DROP TABLE, DROP COLUMN, and SET NOT NULL. At host startup the blocked migration **aborts startup** — the host fails to boot rather than run on a stale schema.

```csharp
// This blocks breaking changes
builder.UsePragmaticMigrations();

// This allows them
builder.UsePragmaticMigrations(m =>
{
    m.Force();
});
```

**Best practice**: Never use `Force()` unconditionally in production. Instead:

1. Run with `DryRun()` to inspect what would change
2. Review the breaking changes
3. Back up the database
4. Apply with `Force()` in a controlled deployment

For CI/CD pipelines, consider a two-stage approach: dry-run in pre-deploy, force in deploy.

---

## 4. Schema Hash Mismatch After Manual Database Changes

**Symptom**: Migrations keep running on every startup even though the schema looks correct. Or migrations report "3 changes" when you expect 0.

**Cause**: Someone modified the database manually (added a column, changed a type, created an index) outside of the migration pipeline. The introspected schema no longer matches the SG-generated `SchemaVersion` hash.

**How to diagnose**: Run with `DryRun()` and inspect the `AppliedChanges` list:

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.DryRun();
});
```

Check the logs for unexpected changes like:

```
[safe] DROP INDEX "IX_Manual_Index"
[BREAKING] DROP COLUMN "ManuallyAddedColumn"
```

**Fix**: Either:

1. Remove the manual changes from the database to match the desired schema
2. Add the manual columns/indexes to your `[Entity]` definitions so the SG includes them in the desired schema
3. If the object is genuinely owned by another system, a DBA, or a database extension, exclude it: `m.ExcludeTable("name")` — the diff engine then ignores it entirely. When the whole database is shared, `m.ManageDeclaredTablesOnly()` is the blanket version: nothing this host did not declare is ever proposed for deletion

**Rule**: Never modify schema managed by Pragmatic.Migrations by hand. Managed schema changes flow from `[Entity]` attributes; anything owned elsewhere should be declared with `ExcludeTable`.

---

## 5. SET NOT NULL on Columns with Existing NULL Values

**Symptom**: `Migration failed at step 3/7: ALTER COLUMN "Email" SET NOT NULL` with error: `column "Email" of relation "Users" contains null values`.

**Cause**: You changed a property from `string?` to `string` (nullable to non-nullable), but existing rows have NULL values in that column.

**Fix**: Before deploying the schema change:

```sql
-- Update NULLs to a default value first
UPDATE "Users" SET "Email" = '' WHERE "Email" IS NULL;
```

Then deploy the entity change. The migration runner provides context-aware suggestions for this exact case:

```
Suggestions:
  - SET NOT NULL failed -- the column may contain NULL values
  - Update existing NULL values before applying this change
  - All changes have been rolled back -- the database is in its original state
```

---

## 6. Concurrent Migrations Without Leader Election

**Symptom**: Duplicate table errors, deadlocks, or partial schema states when multiple host instances start simultaneously.

**Cause**: If you bypass the standard runner or disable leader election, multiple instances can execute migrations at the same time.

**How it works correctly**: `DatabaseLeaderElection` uses the `__PragmaticLock` table to ensure only one instance runs migrations. This is automatic -- you do not need to configure it.

**When it can go wrong**:

- Custom `IMigrationLeaderElection` that always returns `true`
- Running migrations from a script outside the host process
- Testing with `AlwaysLeaderElection` leaking into production (SQLite always uses it — a single-file database has nothing to coordinate)

**Rule**: In production with multiple instances, always use the default `DatabaseLeaderElection`. It requires zero external infrastructure.

---

## 7. Filtering the Wrong Database Name

**Symptom**: Migrations are skipped for the database you want to migrate. Logs show: `[MyDatabase] Skipped (not in database filter)`.

**Cause**: `OnlyDatabase<T>()` matches against the type **name** (not namespace). If your database class is `Showcase.Infrastructure.ShowcaseAppDatabase`, the filter matches `"ShowcaseAppDatabase"`.

```csharp
// Wrong: using a type that does not match any SchemaVersion.DatabaseName
m.OnlyDatabase<MyOtherDatabase>();

// Correct: type name must match the database used in [Include<TModule, TDatabase>]
m.OnlyDatabase<ShowcaseAppDatabase>();
```

**How to verify**: Check the SG-generated `SchemaVersion` metadata. The `DatabaseName` property is derived from the type used in your topology attributes.

---

## 8. Ignoring the Audit Table in Backup/Restore

**Symptom**: After restoring a database backup, migrations re-run changes that were already applied, causing errors or duplicate data.

**Cause**: The `__PragmaticSchema` audit table was excluded from the backup, or the backup predates the last migration. The runner has no record of previous migrations and treats the schema as if it needs updating.

**Fix**: nothing, usually — and this is worth being precise about, because the audit table is a
*record*, not an input. The runner decides what to do by comparing the desired schema against the
live database, never against the audit history: restoring a backup without `__PragmaticSchema` costs
you the history, not correctness. A restored database that already matches the entities produces an
empty diff and no changes.

What the restore *can* cost you is the record of which `IDataMigration`s already ran — that one IS
an input:

1. Restore the backup, including `__PragmaticDataMigrations`.
2. Run with `DryRun()` to see what the runner intends to do.
3. Only if a data migration must not run again, insert its name by hand:

```sql
INSERT INTO "__PragmaticDataMigrations" ("Name", "DurationMs") VALUES ('2026-05_BackfillOrderStatus', 0);
```

**Best practice**: include `__PragmaticSchema` (audit history) and `__PragmaticDataMigrations`
(run-once tracking) in backups. `__PragmaticLock` holds no durable state — it is recreated on demand.

---

---

## 9. Trying to Supply `SchemaVersion.Hash`

**Symptom**: Code that constructs a `SchemaVersion` with a hash argument stops compiling, or a
hand-written `__PragmaticSchema` row carries a hash that never matches anything.

**Cause**: the hash is **derived**, not supplied. It is the canonical identity of the schema,
computed from the tables themselves — which is exactly what makes the compile-time schema and an
introspected one comparable.

**What to do instead**: compare hashes freely, they mean something.

```csharp
var current = await introspector.IntrospectAsync(connection, ct);
if (current.Hash == MyDbSchema.Current.Hash) { /* the database is up to date */ }
```

That is what the runner's fast path does and what `pragmatic-migrate status` reports. To learn
*what* differs rather than *whether* it differs, ask the diff engine.

---

## Quick Reference

| # | Mistake | Fix |
|---|---------|-----|
| 1 | Missing `UsePragmaticMigrations()` | Add call in `Program.cs` |
| 2 | Provider package not in host | Add `Npgsql` / `Microsoft.Data.SqlClient` to host `.csproj` |
| 3 | Breaking changes without `Force` | Use `DryRun()` first, then `Force()` for controlled deploys |
| 4 | Manual DB changes cause hash mismatch | Never modify schema manually; update `[Entity]` instead |
| 5 | SET NOT NULL on columns with NULLs | Update existing NULLs before deploying the change |
| 6 | Concurrent migrations | Use default `DatabaseLeaderElection` (automatic) |
| 7 | Wrong database filter type name | Match the type used in `[Include<TModule, TDatabase>]` |
| 8 | Tracking tables excluded from backup | Back up `__PragmaticDataMigrations` (run-once state) and `__PragmaticSchema` (history) |
| 9 | Treating `Hash` as something you set | It is derived from the schema; compare it, do not supply it |
