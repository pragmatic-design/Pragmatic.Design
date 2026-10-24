using System.Globalization;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     Database sequences, as the repository's <c>SequenceValueProvider</c> hands them out: concurrency-safe numbers with gaps (a
///     rolled-back insert still consumes one), never a value twice. PostgreSQL and SQL Server have sequences; SQLite has none and is
///     emulated with a counter table (<c>__pragmatic_sequences</c>, one row per sequence, <c>UPDATE … RETURNING</c> atomic under
///     SQLite's single writer). The DDL is the application's (a migration, a schema script): the session reads values only, so the race
///     two first uses would run on <c>CREATE … IF NOT EXISTS</c> never happens at runtime. One difference of the emulation: a value taken
///     inside a transaction that rolls back is handed out again on SQLite (the counter row rolls back with it), where a sequence keeps it consumed.
/// </summary>
public static class SqlSequences
{
    /// <summary>The emulation table on SQLite.</summary>
    public const string SqliteTable = "__pragmatic_sequences";

    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>The statements that create the sequence when it is not there, starting at <paramref name="start" /> (an existing one is left as it is).</summary>
    public static IReadOnlyList<string> CreateSql(SqlDialect dialect, string name, long start = 1)
    {
        Validate(name);
        var first = start.ToString(CultureInfo.InvariantCulture);
        return dialect switch
        {
            SqlDialect.Sqlite =>
            [
                $"CREATE TABLE IF NOT EXISTS \"{SqliteTable}\" (\"name\" TEXT NOT NULL PRIMARY KEY, \"value\" INTEGER NOT NULL)",
                $"INSERT OR IGNORE INTO \"{SqliteTable}\" (\"name\", \"value\") VALUES ('{name}', {(start - 1).ToString(CultureInfo.InvariantCulture)})",
            ],
            SqlDialect.PostgreSql => [$"CREATE SEQUENCE IF NOT EXISTS \"{name}\" START WITH {first}"],
            SqlDialect.SqlServer => [$"IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'{name}') CREATE SEQUENCE [{name}] AS bigint START WITH {first} INCREMENT BY 1"],
            SqlDialect.MySql =>
            [
                $"CREATE TABLE IF NOT EXISTS \"{SqliteTable}\" (\"name\" VARCHAR(128) NOT NULL PRIMARY KEY, \"value\" BIGINT NOT NULL)",
                $"INSERT IGNORE INTO \"{SqliteTable}\" (\"name\", \"value\") VALUES ('{name}', {(start - 1).ToString(CultureInfo.InvariantCulture)})",
            ],
            SqlDialect.Oracle => [$"CREATE SEQUENCE IF NOT EXISTS \"{name}\" START WITH {first}"],
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };
    }

    /// <summary>The statement that drops the sequence when it is there (on SQLite, its row of the counter table; nothing when the table itself is not there yet).</summary>
    public static string DropSqlIfThere(SqlDialect dialect, string name)
    {
        Validate(name);
        return dialect switch
        {
            SqlDialect.Sqlite => $"CREATE TABLE IF NOT EXISTS \"{SqliteTable}\" (\"name\" TEXT NOT NULL PRIMARY KEY, \"value\" INTEGER NOT NULL); DELETE FROM \"{SqliteTable}\" WHERE \"name\" = '{name}'",
            SqlDialect.PostgreSql => $"DROP SEQUENCE IF EXISTS \"{name}\"",
            SqlDialect.SqlServer => $"IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'{name}') DROP SEQUENCE [{name}]",
            SqlDialect.MySql => $"CREATE TABLE IF NOT EXISTS \"{SqliteTable}\" (\"name\" VARCHAR(128) NOT NULL PRIMARY KEY, \"value\" BIGINT NOT NULL); DELETE FROM \"{SqliteTable}\" WHERE \"name\" = '{name}'",
            SqlDialect.Oracle => $"DROP SEQUENCE IF EXISTS \"{name}\"",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };
    }

    /// <summary>True where the sequence is the counter table (SQLite, MySQL): the statements take the sequence's name as their first parameter.</summary>
    internal static bool IsEmulated(SqlDialect dialect) => dialect is SqlDialect.Sqlite or SqlDialect.MySql;

    /// <summary>
    ///     The statement reading one value: <c>nextval</c>, <c>NEXT VALUE FOR</c>, or the counter's increment (SQLite reads it back with
    ///     RETURNING; MySQL has none: the new value goes through <c>LAST_INSERT_ID(expr)</c> and is read in the same command); its parameter kinds (the name, where emulated).
    /// </summary>
    internal static (string Sql, SqlParamKind[] Kinds) NextSql(SqlDialect dialect, string name) => dialect switch
    {
        SqlDialect.Sqlite => ($"UPDATE \"{SqliteTable}\" SET \"value\" = \"value\" + 1 WHERE \"name\" = @p0 RETURNING \"value\"", [SqlParamKind.String]),
        SqlDialect.MySql => ($"UPDATE \"{SqliteTable}\" SET \"value\" = LAST_INSERT_ID(\"value\" + 1) WHERE \"name\" = @p0; SELECT LAST_INSERT_ID() WHERE ROW_COUNT() > 0 OR @p0 IS NULL", [SqlParamKind.String]),
        SqlDialect.PostgreSql => ($"SELECT nextval('\"{name}\"')", []),
        SqlDialect.Oracle => ($"SELECT \"{name}\".NEXTVAL FROM dual", []),
        _ => ($"SELECT NEXT VALUE FOR [{name}]", []),
    };

    /// <summary>
    ///     The statement reading a range of <c>@p0</c> values (the count first on SQLite, after the name): one row per value on PostgreSQL
    ///     (<c>generate_series</c>); the range's first value and the increment on SQL Server (<c>sp_sequence_get_range</c>); the counter
    ///     after the increment on SQLite (the range ends there).
    /// </summary>
    internal static (string Sql, SqlParamKind[] Kinds) RangeSql(SqlDialect dialect, string name) => dialect switch
    {
        SqlDialect.Sqlite => ($"UPDATE \"{SqliteTable}\" SET \"value\" = \"value\" + @p1 WHERE \"name\" = @p0 RETURNING \"value\"", [SqlParamKind.String, SqlParamKind.Int32]),
        SqlDialect.MySql => ($"UPDATE \"{SqliteTable}\" SET \"value\" = LAST_INSERT_ID(\"value\" + @p1) WHERE \"name\" = @p0; SELECT LAST_INSERT_ID() WHERE ROW_COUNT() > 0 OR @p0 IS NULL", [SqlParamKind.String, SqlParamKind.Int32]),
        SqlDialect.PostgreSql => ($"SELECT nextval('\"{name}\"') FROM generate_series(1, @p0)", [SqlParamKind.Int32]),
        SqlDialect.Oracle => ($"SELECT \"{name}\".NEXTVAL FROM dual CONNECT BY LEVEL <= @p0", [SqlParamKind.Int32]),
        _ => ($"DECLARE @first sql_variant, @increment sql_variant; EXEC sp_sequence_get_range @sequence_name = N'[{name}]', @range_size = @p0, @range_first_value = @first OUTPUT, @sequence_increment = @increment OUTPUT; SELECT CAST(@first AS bigint), CAST(@increment AS bigint)", [SqlParamKind.Int32]),
    };

    /// <summary>A sequence name is an identifier (letters, digits, underscore): it is written into the statement, never bound.</summary>
    internal static void Validate(string name)
    {
        if (name is null || !Identifier.IsMatch(name))
            throw new ArgumentException($"'{name}' is not a sequence name: letters, digits and underscores, starting with a letter or an underscore.", nameof(name));
    }
}
