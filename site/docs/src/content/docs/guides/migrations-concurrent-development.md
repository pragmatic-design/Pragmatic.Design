---
title: "Migrations in Team Development"
description: "How `Pragmatic.Migrations` behaves when several developers work in parallel on different branches and then merge. Read the Pragmatic.Migrations README first."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/docs/howto/migrations-concurrent-development.md
sidebar:
  order: 4
---
How `Pragmatic.Migrations` behaves when several developers work in parallel on different branches and then merge. Read [the Pragmatic.Migrations README](/modules/migrations/overview/) first.

## Mental model

`Pragmatic.Migrations` does **not** use numbered migration files like EF Core. It works by **declarative diff**:

1. **Compile time**: the source generator scans the `[Entity]` entities and produces a *desired schema* (`SchemaVersion`), identified by a hash.
2. **Runtime**: at startup the runner introspects the database's real schema, computes the diff towards the desired schema and applies it.

There are no migration files: the "target state" is always and only what the code's entities describe *now*.

## What happens with concurrent branches

Typical scenario:

- **Branch A** adds the `Invoice` entity.
- **Branch B** adds the `Payment` entity.
- Both start from `main` and get merged.

Unlike EF Core, where two migration files create an explicit git conflict on the `ModelSnapshot`, here **the git merge concerns only the entity classes**. If the merge is correct (both the `Invoice` and `Payment` classes end up on `main`), the desired schema contains both and everything works: the diff produces two `CreateTable`s, non-breaking, applied without intervention.

The risk appears when the **merge is resolved badly**: if one of the two entity classes is lost while resolving conflicts (or a file is not added), the resulting desired schema **will not contain that entity**. Git reports nothing: there is no migration file to collide on.

## The safety net: the breaking-change gate

This does **not** turn into silent data loss. When the runner computes the diff between the desired schema (without the lost entity) and the real database (which already has the table), it produces a `DropTable`.

`DropTable` and `DropColumn` are marked `IsBreaking: true`. The runner (`MigrationRunner`) **blocks** execution if the diff contains breaking changes and `MigrationOptions.Force` is `false`:

```
[App] Blocked: 1 breaking changes. Use Force=true.
  1 breaking change(s) detected — these may cause data loss
  Run with DryRun=true to inspect the SQL before applying
  Use Force=true to apply breaking changes
```

`Force` defaults to `false` and `app.UsePragmaticMigrations()` does not enable it. So a merge that makes an entity disappear **stops the application from starting** instead of dropping the table.

### Change taxonomy

| Change | Breaking? | Notes |
|---|---|---|
| `CreateTable` | No | |
| `AddColumn` | No | |
| `RenameColumn` | No | Detected only with `[RenamedFrom]` on the property |
| `AlterColumnDefault` | No | |
| `DropTable` | **Yes** | |
| `DropColumn` | **Yes** | |
| `AlterColumnType` | Yes if narrowing | E.g. `varchar(256)` → `varchar(100)` |
| `AlterColumnNullability` | Yes if it becomes `NOT NULL` | Existing `NULL` data would violate it |
| `AlterPrimaryKey` | **Yes** | Removes a uniqueness guarantee; fails if the rows do not satisfy the new key |
| `AddColumn` NOT NULL without a default | **Yes** | Existing rows cannot satisfy it |

`AlterColumnType` is narrowing also when the type *family* changes (`varchar(50)` → `int`,
`timestamptz` → `text`): the conversion fails or reinterprets every value already stored.

## The committed snapshot: making the conflict visible

The structural gap described above (git has nothing to collide on) is closed by giving git
something to collide on. Enable the schema snapshot in the host project:

```xml
<PropertyGroup>
  <PragmaticSchemaSnapshot>true</PragmaticSchemaSnapshot>
</PropertyGroup>
```

After every build `schema/<database>.schema.json` is regenerated, and it must be **committed**. From
then on:

- a PR that touches the schema shows the schema diff, readable in review;
- two branches that modify the same table produce an ordinary merge conflict on the JSON file;
- a merge that loses an entity shows immediately, because the table disappears from the snapshot.

The CI gate:

```bash
pragmatic-migrate snapshot --assembly path/to/MyApp.dll --output schema
git diff --exit-code schema/
```

A non-empty diff means the entities changed without regenerating the snapshot, or that two
branches modified the schema incompatibly.

## Recommended workflow

1. **Committed snapshot + CI gate.** The upstream net: it makes the schema conflict explicit in
   git instead of discovering it at startup.
2. **`DryRun` in CI.** Run the migration with `MigrationOptions { DryRun = true }` in the pipeline against a DB aligned with production. It prints the list of changes; if unexpected `DropTable`/`DropColumn`s appear, the merge lost something.
3. **Frequent rebase on `main`.** It shrinks the window in which two branches diverge on the schema.
4. **One entity per PR, when possible.** It minimizes conflicts that touch the schema.
5. **Treat the breaking-change block as a signal, not an obstacle.** If at startup you see `DROP TABLE X` and nobody *intentionally* removed the `X` entity, it is almost certainly an incomplete merge: do **not** set `Force=true`, restore the missing entity.
6. **`Force=true` only for intentional removals**, and after checking the SQL with `DryRun`.
7. **A backup before every `Force`**, as for any destructive DDL.

## Known limits

- There is no compile-time detection of "the code no longer declares an entity that existed on `main`": the source generator sees only the current state of the code. The safety nets are the committed snapshot (at git/CI level) and the breaking-change gate (at runtime).
- The desired schema depends on the merged code, not on the merge order: two merges that produce the same set of entities produce the same schema.
- Tenant-specific schemas (custom columns for a single tenant) are not supported: the desired schema is global. For per-tenant variations use optional flags/columns at global level.

## In short

Concurrent merging is safe **as long as the git merge of the entity classes is correct**. If it is not, the damage is not silent: the committed snapshot already shows it in review, and the breaking-change gate stops the application anyway, naming exactly which tables and columns would be dropped. The operating rule is a single one: **an unexpected `DROP` at startup = a merge to review, not a `Force` to add.**
