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

## Choosing the engine for a boundary

```csharp
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]     // Pragmatic.Persistence.Entity
public partial class ReportingBoundary;
```

Optional while the compilation references one engine. With two or more, a boundary that names none is
**PRAG0743**; naming an engine the compilation does not reference is **PRAG0744**.

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
| `[SqlDatabase]` | `partial` class | a database whose schema the generator emits for Pragmatic.Migrations: `public static SchemaVersion Schema(SqlDialect dialect)` with the tables of the assembly's entities. Emitted only when the compilation references Pragmatic.Migrations. With several databases, `[StoredIn<TDatabase>]` on an entity says which |
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
