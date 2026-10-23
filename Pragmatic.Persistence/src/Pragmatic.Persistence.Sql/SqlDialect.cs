namespace Pragmatic.Persistence.Sql;

/// <summary>The SQL the database speaks: quoting, paging, and what it stores the .NET types as.</summary>
/// <remarks>
///     Linked into Pragmatic.SourceGenerator as source, where it is internal: a public copy there would make a project that references
///     both the generator and this runtime (the generator's tests) fail with CS0433.
/// </remarks>
#if PRAGMATIC_SOURCE_GENERATOR
internal
#else
public
#endif
    enum SqlDialect
{
    Sqlite,
    PostgreSql,
    SqlServer,

    /// <summary>
    ///     MySQL 8 (and MariaDB where they agree), spoken with <c>sql_mode</c> ANSI_QUOTES and PIPES_AS_CONCAT on the session
    ///     (<c>SqlMySql.Prepare</c>): identifiers quoted with double quotes as everywhere but SQL Server, <c>LIMIT … OFFSET …</c>, no RETURNING.
    /// </summary>
    MySql,

    /// <summary>
    ///     Oracle Database 23ai (BOOLEAN, multi-row VALUES, <c>IF EXISTS</c>, SQL macros, VECTOR): identifiers in double quotes, parameters
    ///     <c>:pN</c> (the text keeps <c>@pN</c> and the session adapts it, <c>SqlOracle.Adapt</c>), <c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c>,
    ///     several statements run as one PL/SQL block returning their rows (<c>DBMS_SQL.RETURN_RESULT</c>).
    /// </summary>
    Oracle,
}

/// <summary>What differs between dialects in the text of a query.</summary>
#if PRAGMATIC_SOURCE_GENERATOR
internal
#else
public
#endif
    static class SqlDialects
{
    /// <summary>Every SQL dialect, in the order the enum names them: what a per-dialect rendering is written for.</summary>
    public static readonly SqlDialect[] All = [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    /// <summary>True where guids are stored as their text (SQLite, Oracle: no uuid type).</summary>
    public static bool GuidsAsText(SqlDialect dialect) => dialect is SqlDialect.Sqlite or SqlDialect.Oracle;

    /// <summary>True where a statement's rows cannot be read back from a write (no RETURNING/OUTPUT as a result set): MySQL, Oracle.</summary>
    public static bool NoReturning(SqlDialect dialect) => dialect is SqlDialect.MySql or SqlDialect.Oracle;

    public static string Quote(SqlDialect dialect, string identifier) => dialect == SqlDialect.SqlServer ? "[" + identifier + "]" : "\"" + identifier + "\"";

    /// <summary>The paging clause after ORDER BY; SQL Server has OFFSET/FETCH and demands an ORDER BY, which the caller supplies or gets a constant one; MySQL's OFFSET needs a LIMIT (the largest).</summary>
    public static string Paging(SqlDialect dialect, string? offsetParameter, string? limitParameter)
    {
        if (offsetParameter is null && limitParameter is null)
            return "";
        if (dialect is SqlDialect.SqlServer or SqlDialect.Oracle)
            return $" OFFSET {offsetParameter ?? "0"} ROWS" + (limitParameter is null ? "" : $" FETCH NEXT {limitParameter} ROWS ONLY");
        var text = limitParameter is null ? " LIMIT -1" : $" LIMIT {limitParameter}";
        if (dialect == SqlDialect.PostgreSql && limitParameter is null)
            text = " LIMIT ALL";
        if (dialect == SqlDialect.MySql && limitParameter is null)
            text = " LIMIT 18446744073709551615";
        return text + (offsetParameter is null ? "" : $" OFFSET {offsetParameter}");
    }

    /// <summary>
    ///     A statement of keys as the right side of <c>IN (…)</c>: as it is, but on MySQL wrapped in a derived table (<c>SELECT * FROM (…) pk</c>),
    ///     which refuses a LIMIT inside an IN subquery and takes one in a derived table.
    /// </summary>
    public static string KeySubquery(SqlDialect dialect, string statement) => dialect == SqlDialect.MySql ? "SELECT * FROM (" + statement + ") pk" : statement;

    /// <summary>String concatenation: <c>||</c> on SQLite and PostgreSQL, <c>+</c> on SQL Server, <c>CONCAT</c> on MySQL (where <c>||</c> is OR unless PIPES_AS_CONCAT is set).</summary>
    public static string Concat(SqlDialect dialect, string left, string right) => dialect switch
    {
        SqlDialect.SqlServer => $"({left} + {right})",
        SqlDialect.MySql => $"CONCAT({left}, {right})",
        _ => $"({left} || {right})",
    };

    /// <summary>
    ///     A RANGE frame with an offset on SQL Server, which frames by ROWS or by UNBOUNDED/CURRENT ROW ranges only: each <c>{rng:…:rng}</c> token (the
    ///     function, the value, the partition, the order, the offset) a correlated subquery over the statement's table under its predicates — the
    ///     rows the window would see: the same partition, an order value within the offset below the row's.
    /// </summary>
    public static string RangeSubqueries(string sql, string table, System.Collections.Generic.IReadOnlyList<string> predicates)
    {
        const string open = "{rng:", close = ":rng}", separator = "|~|";
        if (sql.IndexOf(open, System.StringComparison.Ordinal) < 0)
            return sql;
        static string Inner(string column) => column.Replace("t.\"", "x.\""); // the subquery's row
        var where = new System.Text.StringBuilder();
        foreach (var predicate in predicates)
            where.Append('(').Append(Inner(predicate)).Append(") AND ");
        var text = new System.Text.StringBuilder(sql.Length + 128);
        var from = 0;
        for (var at = sql.IndexOf(open, System.StringComparison.Ordinal); at >= 0; at = sql.IndexOf(open, from, System.StringComparison.Ordinal))
        {
            var end = sql.IndexOf(close, at, System.StringComparison.Ordinal);
            var parts = sql.Substring(at + open.Length, end - at - open.Length).Split(new[] { separator }, System.StringSplitOptions.None);
            var (function, value, partition, order, offset) = (parts[0], parts[1], parts[2], parts[3], parts[4]);
            text.Append(sql, from, at - from);
            text.Append("(SELECT ").Append(function).Append('(').Append(value == "*" ? "*" : Inner(value)).Append(") FROM \"").Append(table).Append("\" x WHERE ").Append(where)
                .Append(Inner(partition)).Append(" = ").Append(partition).Append(" AND ").Append(Inner(order)).Append(" BETWEEN ").Append(order).Append(" - ").Append(offset).Append(" AND ").Append(order).Append(')');
            from = end + close.Length;
        }

        return text.Append(sql, from, sql.Length - from).ToString();
    }

    /// <summary>The window specifications a statement's text holds (<c>{w:PARTITION BY … ORDER BY …:w}</c>, written by <c>Sql.Window</c>), distinct, in order.</summary>
    public static System.Collections.Generic.List<string> WindowSpecs(string sql)
    {
        var specs = new System.Collections.Generic.List<string>();
        for (var at = sql.IndexOf("{w:", System.StringComparison.Ordinal); at >= 0; at = sql.IndexOf("{w:", at + 3, System.StringComparison.Ordinal))
        {
            var end = sql.IndexOf(":w}", at, System.StringComparison.Ordinal);
            if (end < 0)
                break;
            var spec = sql.Substring(at + 3, end - at - 3);
            if (!specs.Contains(spec))
                specs.Add(spec);
        }

        return specs;
    }

    /// <summary>Each window token replaced by its name in the statement's WINDOW clause (<c>w1</c>, <c>w2</c>… in the order of <paramref name="specs" />).</summary>
    public static string NameWindows(string sql, System.Collections.Generic.List<string> specs)
    {
        for (var i = 0; i < specs.Count; i++)
            sql = sql.Replace("{w:" + specs[i] + ":w}", "w" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return sql;
    }

    /// <summary>The window tokens no statement named (a companion, a nested statement, the runtime path) written in place: <c>OVER (spec)</c>.</summary>
    public static string InlineWindows(string sql)
    {
        foreach (var spec in WindowSpecs(sql))
            sql = sql.Replace("{w:" + spec + ":w}", "(" + spec + ")");
        return sql;
    }

    /// <summary>
    ///     The boolean literal tokens of a statement resolved (<c>{bt:1}</c>, <c>{bt:0}</c>, written where a text carries no parameter: a projection's
    ///     column): 1 and 0 on SQL Server and SQLite, TRUE and FALSE elsewhere.
    /// </summary>
    public static string ResolveBooleanLiterals(string sql, SqlDialect dialect)
    {
        if (sql.IndexOf("{bt:", System.StringComparison.Ordinal) < 0)
            return sql;
        var numeric = dialect is SqlDialect.SqlServer or SqlDialect.Sqlite;
        return sql.Replace("{bt:1}", numeric ? "1" : "TRUE").Replace("{bt:0}", numeric ? "0" : "FALSE");
    }

    /// <summary>
    ///     True where a row version (<c>[ConcurrencyAware]</c>) is the database's own: PostgreSQL's <c>xmin</c>, SQL Server's <c>rowversion</c>. Never
    ///     written, compared and read back as a bigint. Elsewhere it is a BIGINT the writes advance.
    /// </summary>
    public static bool NativeRowVersion(SqlDialect dialect) => dialect is SqlDialect.PostgreSql or SqlDialect.SqlServer;

    /// <summary>The row version of the row under <paramref name="alias" /> (empty: the statement's table) as a bigint: <c>xmin</c>, <c>CAST(rowversion AS bigint)</c>, or the column.</summary>
    public static string RowVersion(SqlDialect dialect, string? alias, string column)
    {
        var prefix = string.IsNullOrEmpty(alias) ? "" : alias + ".";
        return dialect switch
        {
            SqlDialect.PostgreSql => $"CAST(CAST({prefix}xmin AS text) AS bigint)",
            SqlDialect.SqlServer => $"CAST({prefix}{Quote(dialect, column)} AS bigint)",
            _ => prefix + Quote(dialect, column),
        };
    }

    /// <summary>The token a row version column is selected through (<c>{rv:t|RowVersion}</c>): resolved per dialect by <see cref="ResolveRowVersions" />.</summary>
    public static string RowVersionSelect(string alias, string column) => "{rv:" + alias + "|" + column + "}";

    /// <summary>The row version tokens of a statement resolved (<see cref="RowVersion" />), before the quoting: the fragment form's double quotes.</summary>
    public static string ResolveRowVersions(string sql, SqlDialect dialect)
    {
        var at = sql.IndexOf("{rv:", System.StringComparison.Ordinal);
        if (at < 0)
            return sql;
        var text = new System.Text.StringBuilder(sql.Length + 32);
        var from = 0;
        while (at >= 0)
        {
            var end = sql.IndexOf('}', at);
            var parts = sql.Substring(at + 4, end - at - 4).Split('|');
            text.Append(sql, from, at - from);
            // The fragment form: quoted with double quotes whatever the dialect (SQL Server's brackets come after).
            text.Append(RowVersion(dialect, parts[0], parts[1]).Replace("[", "\"").Replace("]", "\""));
            from = end + 1;
            at = sql.IndexOf("{rv:", from, System.StringComparison.Ordinal);
        }

        return text.Append(sql, from, sql.Length - from).ToString();
    }

    /// <summary>The token a vector column is selected through (<c>{vt:t."Vector"}</c>): resolved per dialect by <see cref="ResolveVectorCasts" /> into a read as text.</summary>
    public static string VectorSelect(string columnSql) => "{vt:" + columnSql + "}";

    /// <summary>
    ///     The vector select tokens of a statement resolved: a cast to text on PostgreSQL (Npgsql knows no vector type without its plugin) and to
    ///     <c>nvarchar(max)</c> on SQL Server (one reading for every client), the stored text itself on SQLite. Values compare and sort as vectors
    ///     through the distance functions, which cast the other way.
    /// </summary>
    public static string ResolveVectorCasts(string sql, SqlDialect dialect)
    {
        var at = sql.IndexOf("{vt:", System.StringComparison.Ordinal);
        if (at < 0)
            return sql;
        var text = new System.Text.StringBuilder(sql.Length + 32);
        var from = 0;
        while (at >= 0)
        {
            var end = sql.IndexOf('}', at);
            if (end < 0)
                break;
            var column = sql.Substring(at + 4, end - at - 4);
            text.Append(sql, from, at - from);
            text.Append(dialect switch
            {
                SqlDialect.PostgreSql => $"CAST({column} AS text)",
                SqlDialect.SqlServer => $"CAST({column} AS nvarchar(max))",
                SqlDialect.Oracle => $"VECTOR_SERIALIZE({column} RETURNING VARCHAR2(4000))",
                _ => column,
            });
            from = end + 1;
            at = sql.IndexOf("{vt:", from, System.StringComparison.Ordinal);
        }

        text.Append(sql, from, sql.Length - from);
        return text.ToString();
    }
}
