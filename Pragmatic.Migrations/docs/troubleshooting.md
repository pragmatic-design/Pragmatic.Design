# Troubleshooting: Pragmatic.Migrations

FAQ, common errors, leader election issues, and recovery procedures.

---

## Frequently Asked Questions

### Q: How do I see what SQL will be executed before running it?

Use `DryRun()`:

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.DryRun();
});
```

The `MigrationResult.GeneratedSql` contains the full script. The `AppliedChanges` list shows each individual `SchemaChange` with a `Description` and `IsBreaking` flag. The logs show each change with `[safe]` or `[BREAKING]` indicators.

### Q: Can I run migrations for only one database in a multi-database setup?

Yes, use `OnlyDatabase<T>()`:

```csharp
builder.UsePragmaticMigrations(m =>
{
    m.OnlyDatabase<ShowcaseAppDatabase>();
});
```

### Q: Do I need to install any CLI tools?

Not to migrate. Unlike EF Core (`dotnet ef migrations add`) there are no design-time tools and no
migration files: the SG generates the schema metadata at compile time and the runner applies it at
startup.

There *is* an optional CLI, `dotnet tool install -g Pragmatic.Migrations.Cli`, for the jobs that
happen outside the host: `status` and `script` for a DBA review, `apply` for a deploy step,
`snapshot` for the committed schema artifact, `history` to read the audit table. `apply` delegates
to the same runner the host uses, so it cannot drift from startup behaviour; it applies schema
changes only, since data migrations and hooks live in the host's DI container.

### Q: How does it handle EF Core's __EFMigrationsHistory table?

The introspector automatically excludes `__EFMigrationsHistory` from the current schema. If you are migrating from EF Core Migrations to Pragmatic.Migrations, the old history table is ignored.

### Q: What happens on first run with an empty database?

The introspector returns an empty `SchemaVersion`. The diff engine produces a `CreateTable` change for every table. All changes are `IsBreaking = false`, so no `Force` is needed.

### Q: Can I use Pragmatic.Migrations alongside EF Core Migrations?

Technically yes, but it is not recommended. Both systems would compete for schema ownership. If you need to coexist temporarily during migration, use `OnlyDatabase<T>()` to partition which databases each system manages.

---

## Common Errors

### Error: "Cannot create connection: type 'X' not found"

**Full message**: `InvalidOperationException: Cannot create connection: type 'Npgsql.NpgsqlConnection, Npgsql' not found. Ensure the provider NuGet package is referenced.`

**Cause**: The database provider NuGet package is not referenced in the host project.

**Fix**: Add the provider package to the host `.csproj`:

```xml
<PackageReference Include="Npgsql" Version="9.*" />
<!-- or -->
<PackageReference Include="Microsoft.Data.SqlClient" Version="6.*" />
<!-- or -->
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.*" />
```

---

### Error: "Breaking changes detected (N). Use Force=true to apply."

**Cause**: The diff engine found changes that may cause data loss (DROP TABLE, DROP COLUMN, SET NOT NULL, narrowing type changes). At host startup this aborts startup: the host throws `InvalidOperationException` and does not boot.

**Fix**: Inspect the changes first:

```csharp
m.DryRun();
```

Then apply with explicit acknowledgment:

```csharp
m.Force();
```

Review the `MigrationResult.AppliedChanges` to understand exactly what will be dropped or altered.

---

### Error: "column X of relation Y contains null values"

**Cause**: A property was changed from nullable to non-nullable, but existing rows have NULL values.

**Fix**: Update NULLs before deploying:

```sql
UPDATE "Users" SET "Email" = '' WHERE "Email" IS NULL;
```

Then redeploy with the non-nullable property. The runner provides this suggestion automatically in `MigrationResult.Suggestions`.

---

### Error: "Migration failed at step N/M: ADD FOREIGN KEY"

**Cause**: Orphan rows exist that violate the foreign key constraint.

**Fix**:

1. Identify orphan rows:
   ```sql
   SELECT * FROM "Orders"
   WHERE "CustomerId" NOT IN (SELECT "Id" FROM "Customers");
   ```

2. Clean up orphans:
   ```sql
   DELETE FROM "Orders"
   WHERE "CustomerId" NOT IN (SELECT "Id" FROM "Customers");
   ```

3. Re-run the migration.

The runner suggests: "FK creation failed -- orphan rows may exist that violate the constraint."

---

### Error: "Check database user permissions -- the migration user needs DDL privileges"

**Cause**: The database user in the connection string lacks CREATE TABLE, ALTER TABLE, or DROP permissions.

**Fix**: Grant DDL privileges:

```sql
-- PostgreSQL
GRANT ALL ON SCHEMA public TO myuser;

-- SQL Server
ALTER ROLE db_ddladmin ADD MEMBER myuser;
```

For production, use a dedicated migration user with DDL privileges, separate from the application user.

---

### Error: "SQLite cannot apply … as a single statement — it requires a table rebuild"

**Cause**: an internal contract violation, not a user error. SQLite expresses nullability changes,
default changes, type changes and FK add/drop only as a full table rebuild, which the runner drives
through `GetRebuildTableName`. Seeing this means something bypassed that path.

**Fix**: use `MigrationRunner` (or `GenerateScript(diff)`) rather than calling
`GenerateChangeScript(change)` yourself for those changes. The rebuild needs the desired
`TableSchema`, which a single change does not carry.

---

### Error: "Cannot generate the type change for T.C: the target nullability is unknown"

**Cause**: an `AlterColumnType` was constructed without `IsNullable`. SQL Server's `ALTER COLUMN`
restates the entire column definition and treats an omitted NULL/NOT NULL as NULL, so applying it
blindly would silently drop a NOT NULL constraint. The generator refuses instead of guessing.

**Fix**: the diff engine always supplies it; this only appears when a change is hand-built. Pass the
target nullability.

---

### Error: "schema snapshot not regenerated … IOException: … a file with a user-mapped section open"

**Cause**: with `<PragmaticSchemaSnapshot>true</PragmaticSchemaSnapshot>` the build runs `snapshot` after
every build. Windows refuses to overwrite a file while another process holds a memory-mapped view of it
(a git client hashing the working tree, an IDE indexer, a scanner), and those typically open the file
right after the previous build rewrote it. The snapshot is written **only when its content changes**, so
a build that regenerates the same schema touches nothing and cannot hit this; the message appears only
for a real schema change that coincides with such a reader.

**Fix**: build again; the reader has let go by then. Under `-warnaserror` the failure is an error, by
design: a snapshot that did not follow a schema change is exactly what the committed `schema/` folder
exists to catch.

---

## Leader Election Issues

### Multiple instances migrating simultaneously

**Symptom**: Duplicate table errors or deadlocks on startup.

**Cause**: Leader election failed to coordinate. Possible reasons:

1. Different connection strings pointing to different databases
2. Custom `IMigrationLeaderElection` that always returns `true`
3. The `__PragmaticLock` table was dropped manually

**Diagnosis**: Check the logs for:

```
Migration leader elected: {HostId}
Another instance is the migration leader -- waiting
```

If both instances log "leader elected", the lock mechanism is not working correctly.

**Fix**: Verify all instances use the same connection string. Ensure the `__PragmaticLock` table exists. The runner creates it automatically, but manual deletion breaks coordination.

---

### Leader crashed, followers waiting indefinitely

**Symptom**: Host instances stuck at startup, logs show "Waiting for leader to complete migrations..." but never proceed.

**Cause**: The leader process crashed after acquiring the lock but before releasing it. The lock has not expired yet.

**Fix**: Wait for the lock to expire (default: 5 minutes). Or manually release:

```sql
-- PostgreSQL
UPDATE "__PragmaticLock"
SET "HolderId" = NULL, "AcquiredAt" = NULL, "ExpiresAt" = NULL
WHERE "LockName" = 'migration';

-- SQL Server
UPDATE __PragmaticLock
SET HolderId = NULL, AcquiredAt = NULL, ExpiresAt = NULL
WHERE LockName = 'migration';
```

After the lock is released, followers proceed within one polling interval (`LeaderPollInterval`,
2 seconds by default, plus jitter). Each one then verifies the schema before reporting success: if
the leader crashed without migrating, the follower fails with
`Migration leader finished but the schema is still N change(s) behind` instead of starting on a
half-migrated database.

---

### FallbackLeaderElection warning in logs

**Log message**: `Leader election failed — defaulting to NOT leader (fail-safe)`

**Cause**: `DatabaseLeaderElection` threw (connection refused, permission denied, missing lock
table). `FallbackLeaderElection` then reports **not leader**: assuming leadership on a transient
error would let every instance migrate at once (split-brain), so the safe answer is to wait.

**Consequence**: this instance does not migrate. It waits for whoever genuinely holds the lock, then
checks the schema, and if nobody migrated, it fails rather than starting on a stale database:
`Migration leader finished but the schema is still N change(s) behind`.

**Fix**: investigate why `DatabaseLeaderElection` failed. Common causes:

- Connection string invalid for the lock table
- Database not reachable at startup time
- Insufficient permissions to CREATE TABLE

---

## Recovery Procedures

### Recovery After a Failed Migration

When a migration fails, the transaction is rolled back automatically. The database is in its original state. To recover:

1. Check `MigrationResult.FailedChangeIndex` and `FailedChangeSql` to understand what failed
2. Read the `Suggestions` for context-aware guidance
3. Fix the underlying issue (data cleanup, permissions, etc.)
4. Restart the host -- the runner will retry from scratch

### Recovery After Manual Schema Modification

If the database was modified outside the migration pipeline:

1. Run with `DryRun()` to see what the runner thinks needs to change
2. If the changes are correct, run normally
3. If the runner wants to undo manual changes, either:
   - Revert the manual changes
   - Update `[Entity]` definitions to match the manual changes

### Recovery After Database Restore

After restoring a backup:

1. Run with `DryRun()` to see what the runner intends to do. The decision comes from comparing the
   entities against the live database, never from the audit table, so a restored database that
   already matches produces an empty diff.
2. If the backup predates the current model, let the migration apply the difference.
3. Make sure `__PragmaticDataMigrations` came back with the backup. That table *is* an input: without
   it, every `IDataMigration` is considered un-run and will execute again.

### Stuck __PragmaticLock

If the lock table is corrupted or the holder ID does not match any running instance:

```sql
-- Reset the lock (all providers)
UPDATE "__PragmaticLock"
SET "HolderId" = NULL, "AcquiredAt" = NULL, "ExpiresAt" = NULL
WHERE "LockName" = 'migration';
```

Or drop and let the runner recreate it:

```sql
DROP TABLE IF EXISTS "__PragmaticLock";
```

---

## Diagnostic Checklist

When migrations are not working as expected, check these in order:

| # | Check | How |
|---|-------|-----|
| 1 | Is `UsePragmaticMigrations()` called? | Search `Program.cs` for the call |
| 2 | Is the provider package referenced? | Check host `.csproj` for `Npgsql` / `Microsoft.Data.SqlClient` |
| 3 | Is the connection string correct? | Verify `appsettings.json` `ConnectionStrings` section |
| 4 | Is the database reachable? | Test with `psql` / `sqlcmd` / `sqlite3` directly |
| 5 | Are migration logs appearing? | Set `Pragmatic.Migrations` log level to `Debug` |
| 6 | Is leader election working? | Check logs for "leader elected" / "waiting" messages |
| 7 | Are there breaking changes? | Run with `DryRun()` to inspect |
| 8 | Is the audit table intact? | Query `__PragmaticSchema` for recent entries |

---

## Log Level Configuration

For detailed migration diagnostics:

```json
{
  "Logging": {
    "LogLevel": {
      "Pragmatic.Migrations": "Debug"
    }
  }
}
```

At `Debug` level, the runner logs each individual change being applied. At `Information` level (default), it logs the start, diff summary, and completion.
