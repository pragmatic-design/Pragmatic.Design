namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>
///     The full-text indexes of an entity's <see cref="FullTextAttribute" /> columns, from its map: what a migration runs once.
///     SQLite: per column an FTS5 table <c>{Table}_{Column}_fts</c> over the table's rows (external content: the text is not stored twice), kept by
///     triggers in the writing transaction, and filled from the rows already there. SQL Server: the full-text catalog <c>pragmatic_ft</c> and one
///     full-text index on the table's marked columns, keyed by its primary key, changes tracked automatically (the server populates it after the
///     commit, in the background: a row is searchable once its population ran). MySQL: a FULLTEXT index on the marked columns (InnoDB: committed
///     rows). Oracle: a CONTEXT index per column, synchronised on commit (Oracle Text, CTXSYS). PostgreSQL's tsvector needs no index to answer
///     (a GIN index on the expression is the application's choice).
/// </summary>
public static class SqlFullText
{
    /// <summary>The full-text catalog SQL Server's indexes go in.</summary>
    public const string Catalog = "pragmatic_ft";

    public static IReadOnlyList<string> CreateSql<T>(SqlDialect dialect)
    {
        var map = SqlEntityMap<T>.Current;
        var marked = Array.FindAll(map.Columns, c => c.FullTextTable is not null);
        if (marked.Length == 0)
            return [];
        string Q(string name) => SqlDialects.Quote(dialect, name);
        var table = Q(map.Table);
        switch (dialect)
        {
            case SqlDialect.Sqlite:
            {
                var statements = new List<string>();
                foreach (var column in marked)
                {
                    var fts = Q(SqlCatalog.FtsTable(map.Table, column.Column));
                    var name = Q(column.Column);
                    var trigger = map.Table + "_" + column.Column + "_fts";
                    statements.Add($"CREATE VIRTUAL TABLE {fts} USING fts5({name}, content={Literal(map.Table)})");
                    statements.Add($"CREATE TRIGGER {Q(trigger + "_insert")} AFTER INSERT ON {table} BEGIN INSERT INTO {fts} (rowid, {name}) VALUES (NEW.rowid, NEW.{name}); END");
                    statements.Add($"CREATE TRIGGER {Q(trigger + "_delete")} AFTER DELETE ON {table} BEGIN INSERT INTO {fts} ({fts}, rowid, {name}) VALUES ('delete', OLD.rowid, OLD.{name}); END");
                    statements.Add($"CREATE TRIGGER {Q(trigger + "_update")} AFTER UPDATE OF {name} ON {table} BEGIN INSERT INTO {fts} ({fts}, rowid, {name}) VALUES ('delete', OLD.rowid, OLD.{name}); INSERT INTO {fts} (rowid, {name}) VALUES (NEW.rowid, NEW.{name}); END");
                    statements.Add($"INSERT INTO {fts} ({fts}) VALUES ('rebuild')"); // the rows already in the table
                }

                return statements;
            }
            case SqlDialect.SqlServer:
            {
                if (map.HasCompositeKey)
                    throw new NotSupportedException($"\"{map.Table}\" has a composite key: a full-text index is keyed by a single-column unique index.");
                // The key index is the primary key's, named by the server: read from the catalog, the index created through dynamic SQL.
                var columns = string.Join(", ", Array.ConvertAll(marked, c => Q(c.Column)));
                return
                [
                    $"IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{Catalog}') CREATE FULLTEXT CATALOG [{Catalog}]",
                    $"DECLARE @key sysname = (SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('{map.Table}') AND is_primary_key = 1); "
                    + $"EXEC('CREATE FULLTEXT INDEX ON {table} ({columns}) KEY INDEX [' + @key + '] ON [{Catalog}] WITH CHANGE_TRACKING AUTO')",
                ];
            }
            case SqlDialect.MySql:
                return [$"CREATE FULLTEXT INDEX {Q("FT_" + map.Table)} ON {table} ({string.Join(", ", Array.ConvertAll(marked, c => Q(c.Column)))})"];
            case SqlDialect.Oracle:
                return Array.ConvertAll(marked, c => $"CREATE INDEX {Q("FT_" + map.Table + "_" + c.Column)} ON {table} ({Q(c.Column)}) INDEXTYPE IS CTXSYS.CONTEXT PARAMETERS ('SYNC (ON COMMIT)')");
            default:
                return [];
        }
    }

    /// <summary>SQLite's FTS5 tables dropped (their triggers go with the table); the other engines' indexes go with their table.</summary>
    public static IReadOnlyList<string> DropSql<T>(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite)
            return [];
        var map = SqlEntityMap<T>.Current;
        return Array.ConvertAll(Array.FindAll(map.Columns, c => c.FullTextTable is not null), c => $"DROP TABLE IF EXISTS {SqlDialects.Quote(dialect, SqlCatalog.FtsTable(map.Table, c.Column))}");
    }

    private static string Literal(string text) => "'" + text.Replace("'", "''") + "'";
}
