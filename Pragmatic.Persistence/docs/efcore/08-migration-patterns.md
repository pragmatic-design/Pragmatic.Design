# Migration Patterns

Generated persistence features change both code and schema.

That is powerful, but it also means a new attribute can have production impact even when the C# diff looks small.

This guide explains safe rollout patterns for the most important schema-affecting attributes, with special attention to brownfield databases that already contain data.

## How the schema actually gets applied

Before the patterns, the mechanism, because it changes what "a migration" is here.

`Pragmatic.Migrations` has **no migration files and no EF Core migrations**. The source generator emits
the desired schema from your `[Entity]` declarations; at startup the runtime introspects the database,
diffs the two, and executes ordered, idempotent, provider-specific SQL inside one transaction — if a
change fails, the whole run rolls back.

```csharp
await PragmaticApp.RunAsync(args, builder => builder.UsePragmaticMigrations());
```

Three consequences for everything below:

- **You never author the `AddColumn` calls.** The snippets in this guide describe *what the diff will
  do*, written in EF migration syntax because it reads clearly — not code for you to write.
- **A two-release rollout is staged in the attributes**, not in migration files: release 1 adds the
  attribute whose columns are additive, release 2 tightens.
- **Backfill is yours.** The diff creates and alters structure; it does not populate rows. A backfill
  is a job, a seeder or a script you write and run between the two releases.

The [migrations guide](../../../Pragmatic.Migrations/README.md) has the provider matrix and the SQLite
table-rebuild caveat.

## First principle: separate code rollout from data assumptions

For novice teams, the safest migration habit is:

1. Add nullable or defaulted columns first.
2. Backfill existing rows.
3. Switch application behavior to rely on the new columns.
4. Tighten constraints only after the data is clean.

This usually produces two smaller releases instead of one risky release.

## Safe rollout checklist

Before adding a persistence attribute to an entity that already has rows in production, ask:

1. Will this add columns?
2. Will this add indexes or uniqueness constraints?
3. Will this change query filtering?
4. Will this change delete semantics?
5. Will this require backfilling old rows before the app can behave correctly?

If the answer to any of these is "yes", plan the migration explicitly instead of relying on a single schema diff.

## Adding `[SoftDelete]` to an existing entity

`[SoftDelete]` adds generated members such as:

- `IsDeleted`
- `DeletedAt`
- `DeletedBy`

It also changes behavior:

- repository `Remove(...)` becomes a soft-delete metadata update
- generated query filters start hiding rows where `IsDeleted == true`

### Safe pattern

Release 1:

1. Add the columns with safe defaults.
2. Keep existing rows visible by default.
3. Deploy and verify that reads and writes still behave correctly.

What the diff will add — again, you do not write this:

```csharp
migrationBuilder.AddColumn<bool>(
    name: "IsDeleted",
    table: "Orders",
    type: "bit",
    nullable: false,
    defaultValue: false);

migrationBuilder.AddColumn<DateTimeOffset>(
    name: "DeletedAt",
    table: "Orders",
    nullable: true);

migrationBuilder.AddColumn<string>(
    name: "DeletedBy",
    table: "Orders",
    maxLength: 200,
    nullable: true);
```

Why the default matters:

- existing rows should not disappear from queries after deployment
- a non-nullable `IsDeleted` column needs an explicit value for historical rows

### Behavioral warning

Once the feature is active, support and admin tooling may need a way to see deleted rows again.

Plan that at the same time:

- document use of `IQueryFilterToggle`
- add admin or maintenance queries that can disable `{Entity}.SoftDeleteFilter`
- update tests so the team knows how to verify hidden rows

## Adding `[Auditable]` to an existing entity

`[Auditable]` adds:

- `CreatedAt`
- `CreatedBy`
- `UpdatedAt`
- `UpdatedBy`

The main brownfield risk is historical data. Existing rows usually do not have values for these fields.

### Safe pattern

Release 1:

1. Add the columns as nullable.
2. Deploy the new code so new writes begin populating the fields.

Release 2:

1. Backfill old rows where needed.
2. Make selected columns non-nullable only if the business truly requires it.

What the diff will add:

```csharp
migrationBuilder.AddColumn<DateTimeOffset>(
    name: "CreatedAt",
    table: "Orders",
    nullable: true);

migrationBuilder.AddColumn<string>(
    name: "CreatedBy",
    table: "Orders",
    maxLength: 200,
    nullable: true);

migrationBuilder.AddColumn<DateTimeOffset>(
    name: "UpdatedAt",
    table: "Orders",
    nullable: true);

migrationBuilder.AddColumn<string>(
    name: "UpdatedBy",
    table: "Orders",
    maxLength: 200,
    nullable: true);
```

Then backfill with a script or migration step appropriate for your provider.

Examples of acceptable backfill strategies:

- set `CreatedAt` to the row creation timestamp if you already store one elsewhere
- set `CreatedAt` to the deployment timestamp as a pragmatic fallback
- set `CreatedBy` to a sentinel like `"migration"` when the original author is unknowable

Do not pretend historical values are authoritative if they are only inferred.

## Adding `[ConcurrencyAware]`

This feature typically introduces a concurrency token such as a row version or equivalent provider-mapped value.

Brownfield risks:

- existing update flows may start throwing concurrency exceptions
- API clients may need to round-trip the token

Safe pattern:

1. Add the concurrency column.
2. Update write paths and API contracts to carry the token.
3. Add integration tests that prove stale updates fail in a controlled way.

Do not add concurrency control to a write-heavy area without also reviewing the user-facing retry story.

## Adding `[LogicKey]` to existing data

`[LogicKey]` is appealing because it creates a first-class business lookup, but on an existing table it often implies a new unique index.

Brownfield risks:

- duplicate historical values
- null or blank values
- callers assuming case sensitivity that differs by provider collation

Safe pattern:

1. Audit the data before adding the unique index.
2. Clean duplicates explicitly.
3. Decide the normalization rule before the constraint goes live.

Useful questions:

- should `"ABC-1"` and `"abc-1"` be treated as the same key?
- should whitespace be trimmed before persistence?
- are blank strings allowed?

Until those answers are explicit, adding `[LogicKey]` is often premature.

## Two-release pattern for risky changes

When an attribute changes both schema and behavior, split the rollout.

Release 1:

- additive schema changes
- compatibility code
- backfill support

Release 2:

- stronger constraints
- cleanup of temporary code paths
- stricter tests and monitoring

This pattern is especially valuable for:

- `[SoftDelete]`
- `[Auditable]`
- `[ConcurrencyAware]`
- new unique business keys

## Migrations and tests should move together

Whenever you introduce one of these attributes, add or update tests that prove the new production rule.

Examples:

- `[SoftDelete]`: deleting an entity hides it from normal reads but not from raw or disabled-filter reads
- `[Auditable]`: `CreatedAt` and `CreatedBy` are populated on insert
- `[LogicKey]`: duplicates fail before or at the database constraint
- `[ConcurrencyAware]`: stale updates fail predictably

If the migration changes behavior and the tests do not change, the rollout is under-specified.

## Pre-deploy checklist

Before shipping a schema-affecting persistence attribute:

1. Review the generated columns and indexes.
2. Decide whether existing data needs backfill.
3. Decide whether filters will hide rows after deployment.
4. Verify admin, support, or migration code paths can still access the data they need.
5. Run integration tests on the provider you actually ship.
6. Confirm rollback strategy if the backfill or the new constraints fail.

## Related guides

- [DbContext Generation](01-dbcontext-generation.md)
- [Testing Generated Persistence](07-testing-generated-persistence.md)
- [Query Filters](../07-query-filters.md)
- [Diagnostics Guide](../14-diagnostics.md)
