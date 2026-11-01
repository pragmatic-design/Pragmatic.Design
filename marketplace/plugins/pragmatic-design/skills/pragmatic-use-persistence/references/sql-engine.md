# The Sql engine

EF Core is not the only engine a boundary can be persisted by. The Sql engine translates LINQ call sites
to SQL **at compile time**: the generator reads each chain once and emits the statement, the binders and
the reader, so nothing builds an expression tree per call.

## Packages

| Package | What it is |
|---|---|
| `Pragmatic.Persistence.Sql` | the bridge: serves Pragmatic's `[Entity]` types on the engine. An application references this one |
| `Pragmatic.Persistence.Sql.Core` | the engine itself: sessions, plans, writers, dialects, its own attributes. Comes with the bridge |

The namespaces carry no `.Core`: the engine's types are in `Pragmatic.Persistence.Sql` and its
sub-namespaces (`.Attributes`, `.Functions`, `.Dialects`, `.Schema`, …).

⚠️ Neither package is in 1.0.0-alpha.3: the engine ships with a later release.

## Pragmatic's entities on the engine

A boundary on the engine keeps its `[Entity]` types, repositories, actions and endpoints. The host opens
the session, and the generated `Add{Boundary}Persistence` registers the rest:

```csharp
services.AddReportingBoundaryPersistence((sp, filters) =>
    new SqlSession(new NpgsqlConnection(connectionString), SqlDialect.PostgreSql) { Filters = filters });
```

- The factory runs once per scope. Pass `filters` on: they carry tenant, owner, soft delete and scopes.
  The session owns and disposes its connection.
- The database's generated `{Database}Schema` is migrated at start by Pragmatic.Migrations, on
  PostgreSQL, SQL Server, SQLite, MySQL and Oracle.
- `IReadRepository<T>.Query()` throws: an `IQueryable` is EF Core's. Write the chain on
  `SqlSession.Query<T>()`, or declare the read (`[Query]`, a specification).
- A module's tables come from its engines package: `Pragmatic.Events.Engines`,
  `Pragmatic.Messaging.Engines`, `Pragmatic.Audit.Engines`, `Pragmatic.Privacy.Engines`.

The full list of what is the same as EF Core and what is not is the module's guide, `docs/21-engines.md`.

## Choosing the engine for a boundary

```csharp
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]     // Pragmatic.Persistence.Entity
public partial class ReportingBoundary;
```

Optional while the compilation references one engine. With two or more, a boundary that names none is
**PRAG0743**; naming an engine the compilation does not reference is **PRAG0744**. A relation to an entity on
another engine keeps only its key: `[ReadAccess<T>]` for that entity, or a many-to-many with it, is **PRAG0745**.

## The engine's own attributes

These are in `Pragmatic.Persistence.Sql.Attributes`, next to the engine's `[Entity]`, `[Table]`,
`[Column]`, `[Key]`, `[Identity]`, `[Version]` and the rest.

| Attribute | On | What it does |
|---|---|---|
| `[DatabaseGenerated]` | property | a column the database fills (a default, a computed column, a trigger): never written by inserts, updates or upserts, read back by key after an insert, and after an update too with `OnUpdate = true` |
| `[FullText]` | property | the column has the engine's full-text index, so `Sql.FullText` / `Sql.FullTextRank` on it are native (SQLite FTS5, SQL Server full-text, MySQL FULLTEXT, Oracle CONTEXT; PostgreSQL needs none). Without it, every engine but PostgreSQL falls back to a case-insensitive `LIKE`: **PRAG3012** |
| `[Indexed]` | property | the column has an index, so a `Where` on it does not scan. Read only when no committed `*.schema.json` among the AdditionalFiles lists the table: then that snapshot's indexes decide. A predicate on an unindexed column is **PRAG3011** (information) |
| `[Interned]` | `string` property | values repeat across rows (a status, a country): the reader hands out one instance per distinct value from a bounded pool, so a page of rows keeps one string per value. It saves what the rows retain, not the allocation while reading |
| `[NotMapped]` | property | the table has no such column (a navigation, a computed value) |
| `[SqlDatabase]` | `partial` class | a database whose schema the generator emits for Pragmatic.Migrations: `public static SchemaVersion Schema(SqlDialect dialect)` with the tables of the assembly's engine `[Entity]` rows. Emitted only when the compilation references Pragmatic.Migrations. With several databases, `[StoredIn<TDatabase>]` on a row says which. A Pragmatic `[Entity]` is never in it: its table is in the host's `DefaultSchema`, and `[StoredIn]` on one is **PRAG3027** |
| `[SqlFunction]` | `static partial` method | an application's SQL function, usable inside a query's lambdas like the catalog's `Sql.*` (below) |

### `[SqlFunction]`

The declared method is implemented by the generator and throws if called in .NET. Its rendering comes
from a private static method with the same name, the dialect first and a `SqlFragment` per argument:

```csharp
using Pragmatic.Persistence.Sql.Attributes;
using Pragmatic.Persistence.Sql.Functions;

internal static partial class AppSql
{
    [SqlFunction]
    public static partial int DaysBetween(DateTimeOffset from, DateTimeOffset to);

    private static SqlFragment DaysBetween(SqlDialect dialect, SqlFragment from, SqlFragment to) => dialect switch
    {
        SqlDialect.SqlServer => Fragment.Call("DATEDIFF", Fragment.Keyword("day"), from, to),
        SqlDialect.PostgreSql => Fragment.Cast(Fragment.Call("TRUNC", Fragment.Extract(DateField.Epoch, to - from) / 86400), SqlType.Integer),
        _ => Fragment.Emulated(Fragment.Cast(Fragment.Call("julianday", to) - Fragment.Call("julianday", from), SqlType.Integer), "dates are ISO text on SQLite"),
    };
}
```

⚠️ The definition is read, never executed. A dialect with no arm is an error where the function is
used: **PRAG3013**.

⚠️ `[SqlDatabase]` covers what Pragmatic.Migrations describes. The views of a hierarchy, the history
and full-text DDL and the sequences stay the text `SqlTemporal`, `SqlFullText` and `SqlSequences`
give, run after the migration.
