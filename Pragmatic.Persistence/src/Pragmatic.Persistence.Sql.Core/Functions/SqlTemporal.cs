namespace Pragmatic.Persistence.Sql.Functions;

/// <summary>
///     The history of a <see cref="TemporalAttribute" /> table where the database keeps none by itself (PostgreSQL, SQLite, MySQL): a table
///     <c>{Table}History</c> with every version of every row, its columns and <c>ValidFrom</c>/<c>ValidTo</c> (UTC; the current version's
///     <c>ValidTo</c> null), written by triggers on the table in the writing transaction, so every write path keeps it (row writes, commands,
///     hand-written SQL). The instant is the transaction's start on PostgreSQL (as SQL Server's system versioning takes it), the statement's on
///     SQLite and MySQL. <c>AsOf(instant)</c> reads the versions valid at the instant. The DDL comes from the entity's map: what a migration runs once.
/// </summary>
public static class SqlTemporal
{
    /// <summary>The history table's name: <c>{Table}History</c>, as SQL Server's system versioning names it in the tests' schema.</summary>
    public static string HistoryTable(string table) => table + "History";

    /// <summary>
    ///     The history table and its triggers, for PostgreSQL, SQLite and MySQL; none for the dialects that keep history by themselves (SQL
    ///     Server's system-versioned table is the table's own DDL, Oracle's Flashback reads any table).
    /// </summary>
    public static IReadOnlyList<string> CreateSql<T>(SqlDialect dialect)
    {
        var map = SqlEntityMap<T>.Current;
        string Q(string name) => SqlDialects.Quote(dialect, name);
        var table = Q(map.Table);
        var history = Q(HistoryTable(map.Table));
        var columns = Array.ConvertAll(map.Columns, c => c.Column);
        string Closes(string row) => string.Join(" AND ", Array.ConvertAll(map.KeyColumns, k => $"{Q(k)} = {row}.{Q(k)}")) + $" AND {Q("ValidTo")} IS NULL";
        switch (dialect)
        {
            case SqlDialect.PostgreSql:
            {
                // LIKE: the table's columns in its order, so the trigger inserts NEW.* followed by the period.
                var now = "(now() AT TIME ZONE 'UTC')";
                var function = Q(HistoryTable(map.Table) + "_track");
                return
                [
                    $"CREATE TABLE {history} (LIKE {table})",
                    $"ALTER TABLE {history} ADD COLUMN {Q("ValidFrom")} timestamp NOT NULL, ADD COLUMN {Q("ValidTo")} timestamp NULL",
                    $"CREATE INDEX {Q("IX_" + HistoryTable(map.Table) + "_Key")} ON {history} ({string.Join(", ", Array.ConvertAll(map.KeyColumns, Q))}, {Q("ValidFrom")})",
                    $"CREATE OR REPLACE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN "
                    + $"IF TG_OP IN ('UPDATE', 'DELETE') THEN UPDATE {history} SET {Q("ValidTo")} = {now} WHERE {Closes("OLD")}; END IF; "
                    + $"IF TG_OP IN ('INSERT', 'UPDATE') THEN INSERT INTO {history} SELECT NEW.*, {now}, NULL; END IF; "
                    + "RETURN NULL; END $$",
                    $"CREATE TRIGGER {Q(map.Table + "_history")} AFTER INSERT OR UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION {function}()",
                ];
            }
            case SqlDialect.Sqlite:
            {
                // The instant as the library writes a DateTime on SQLite (text, sortable): milliseconds, a prefix of the parameter's seven digits.
                var now = "strftime('%Y-%m-%d %H:%M:%f', 'now')";
                var list = string.Join(", ", Array.ConvertAll(columns, Q));
                var values = string.Join(", ", Array.ConvertAll(columns, c => "NEW." + Q(c)));
                var insert = $"INSERT INTO {history} ({list}, {Q("ValidFrom")}) VALUES ({values}, {now});";
                var close = $"UPDATE {history} SET {Q("ValidTo")} = {now} WHERE {Closes("OLD")};";
                return
                [
                    $"CREATE TABLE {history} AS SELECT * FROM {table} WHERE 0",
                    $"ALTER TABLE {history} ADD COLUMN {Q("ValidFrom")} TEXT NOT NULL DEFAULT ''",
                    $"ALTER TABLE {history} ADD COLUMN {Q("ValidTo")} TEXT NULL",
                    $"CREATE INDEX {Q("IX_" + HistoryTable(map.Table) + "_Key")} ON {history} ({string.Join(", ", Array.ConvertAll(map.KeyColumns, Q))}, {Q("ValidFrom")})",
                    $"CREATE TRIGGER {Q(map.Table + "_history_insert")} AFTER INSERT ON {table} BEGIN {insert} END",
                    $"CREATE TRIGGER {Q(map.Table + "_history_update")} AFTER UPDATE ON {table} BEGIN {close} {insert} END",
                    $"CREATE TRIGGER {Q(map.Table + "_history_delete")} AFTER DELETE ON {table} BEGIN {close} END",
                ];
            }
            case SqlDialect.MySql:
            {
                // CREATE TABLE AS: the columns without the keys (a row has many versions); one trigger per event, MySQL's form.
                var now = "UTC_TIMESTAMP(6)";
                var list = string.Join(", ", Array.ConvertAll(columns, Q));
                var values = string.Join(", ", Array.ConvertAll(columns, c => "NEW." + Q(c)));
                var insert = $"INSERT INTO {history} ({list}, {Q("ValidFrom")}) VALUES ({values}, {now})";
                var close = $"UPDATE {history} SET {Q("ValidTo")} = {now} WHERE {Closes("OLD")}";
                return
                [
                    $"CREATE TABLE {history} AS SELECT * FROM {table} WHERE 1 = 0",
                    $"ALTER TABLE {history} ADD COLUMN {Q("ValidFrom")} datetime(6) NOT NULL, ADD COLUMN {Q("ValidTo")} datetime(6) NULL",
                    $"CREATE INDEX {Q("IX_" + HistoryTable(map.Table) + "_Key")} ON {history} ({string.Join(", ", Array.ConvertAll(map.KeyColumns, Q))}, {Q("ValidFrom")})",
                    $"CREATE TRIGGER {Q(map.Table + "_history_insert")} AFTER INSERT ON {table} FOR EACH ROW {insert}",
                    $"CREATE TRIGGER {Q(map.Table + "_history_update")} AFTER UPDATE ON {table} FOR EACH ROW BEGIN {close}; {insert}; END",
                    $"CREATE TRIGGER {Q(map.Table + "_history_delete")} AFTER DELETE ON {table} FOR EACH ROW {close}",
                ];
            }
            default:
                return [];
        }
    }

    /// <summary>The history table dropped (its triggers go with the table; PostgreSQL's function is replaced on the next create).</summary>
    public static IReadOnlyList<string> DropSql<T>(SqlDialect dialect)
        => dialect is SqlDialect.PostgreSql or SqlDialect.Sqlite or SqlDialect.MySql ? [$"DROP TABLE IF EXISTS {SqlDialects.Quote(dialect, HistoryTable(SqlEntityMap<T>.Current.Table))}"] : [];
}
