using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Dialects;

/// <summary>
///     What an Oracle command needs on its way to the provider. The statements are written with <c>@pN</c> parameters as on the other
///     dialects; Oracle names them <c>:pN</c> and the parameter itself <c>pN</c>. A text of several statements (a plan with its include
///     companions, a write batch, a total count) runs as one anonymous PL/SQL block: each SELECT opened on a cursor handed back with
///     <c>DBMS_SQL.RETURN_RESULT</c> (the reader's result sets, in order), each DML executed with its row count kept, so the markers the
///     other dialects' forms use read it: <c>SELECT ROW_COUNT()</c> answers the last count, <c>ROW_COUNT()</c> inside a SELECT is that count,
///     a DML ending <c>RETURNING "Col"</c> keeps the value it returned and <c>SELECT RETURNED</c> answers the values kept since the last one.
///     The provider must bind by name (ODP.NET: <c>OracleConfiguration.BindByName = true</c>, or per command).
/// </summary>
public static partial class SqlOracle
{
    [GeneratedRegex(@"@(p\d+(?:_\d+)?)\b")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\s+RETURNING\s+(""[^""]+"")\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Returning();

    private const string Count = "pragmatic_n";
    private const string Total = "pragmatic_t";

    /// <summary>True for a text <see cref="Text" /> turned into a PL/SQL block: its counts come back as rows, not as the provider's affected count.</summary>
    public static bool IsBlock(string sql) => sql.StartsWith("DECLARE ", StringComparison.Ordinal);

    /// <summary>The rows a block of DML touched: the one row its last cursor answers (the total when the block opens no other).</summary>
    internal static async Task<int> ExecuteCountAsync(DbCommand command, CancellationToken ct)
    {
        using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var count = 0;
        do
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
                count = SqlValue.Count(reader.GetValue(0));
        }
        while (await reader.NextResultAsync(ct).ConfigureAwait(false));
        return count;
    }

    /// <summary>The command made ready for Oracle: its text's parameters as <c>:pN</c>, a multi-statement text as one PL/SQL block, the parameters named without the <c>@</c>. Idempotent.</summary>
    public static void Adapt(DbCommand command)
    {
        command.CommandText = Text(command.CommandText);
        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameter.ParameterName.StartsWith('@'))
                parameter.ParameterName = parameter.ParameterName.Substring(1);
        }
    }

    /// <summary>The batch command's text and parameters adapted the same way.</summary>
    public static void Adapt(DbBatchCommand command)
    {
        command.CommandText = Text(command.CommandText);
        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameter.ParameterName.StartsWith('@'))
                parameter.ParameterName = parameter.ParameterName.Substring(1);
        }
    }

    /// <summary>
    ///     What an Oracle session needs before this library speaks to it: binary sorting and comparison (ODP.NET sets the session's NLS from the
    ///     client's culture, and a linguistic NLS_SORT orders text differently from how <c>&lt;</c> compares it: a keyset page would skip rows), and
    ///     the session's time zone at UTC. Run it on every connection the application opens (a <see cref="SqlSessionPool" /> runs it on the connections it opens).
    /// </summary>
    public const string PrepareSql = "ALTER SESSION SET NLS_SORT = BINARY NLS_COMP = BINARY TIME_ZONE = '+00:00'";

    /// <summary>Sets the session's sorting, comparison and time zone on an open connection (see <see cref="PrepareSql" />).</summary>
    public static void Prepare(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PrepareSql;
        command.ExecuteNonQuery();
    }

    /// <summary>Sets the session's sorting, comparison and time zone on an open connection (see <see cref="PrepareSql" />).</summary>
    public static async Task PrepareAsync(DbConnection connection, CancellationToken ct = default)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PrepareSql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A statement text as Oracle runs it (see the type's summary).</summary>
    public static string Text(string sql)
    {
        sql = Placeholder().Replace(sql, ":$1");
        if (sql.StartsWith("DECLARE ", StringComparison.Ordinal))
            return sql;
        if (!sql.Contains("; ", StringComparison.Ordinal))
            return LockedPage().Match(sql) is { Success: true } page ? LockedPageBlock(page) : sql.TrimEnd().TrimEnd(';');
        return Block(sql);
    }

    [GeneratedRegex(@"^(?<select>.+?) OFFSET 0 ROWS FETCH NEXT (?<take>:p\d+) ROWS ONLY (?<lock>FOR UPDATE(?: SKIP LOCKED| NOWAIT)?)$", RegexOptions.Singleline)]
    private static partial Regex LockedPage();

    /// <summary>
    ///     A locked page (<c>ForUpdate</c> with a <c>Take</c>): Oracle refuses FOR UPDATE beside a row limit (ORA-02014), so the block opens the
    ///     locking cursor without one, fetches the page's row ids (<c>LIMIT</c>: SKIP LOCKED locks a row as it is fetched, the others stay free for
    ///     another worker), and answers the rows of those ids in the statement's order.
    /// </summary>
    private static string LockedPageBlock(Match page)
    {
        var select = page.Groups["select"].Value;
        var from = TopLevel(select, " FROM ");
        if (!select.StartsWith("SELECT ", StringComparison.Ordinal) || from < 0)
            return page.Value; // not a shape this rewrite reads: the database's error stands
        var rest = select.Substring(from); // " FROM … [WHERE …] [ORDER BY …]"
        var order = TopLevel(rest, " ORDER BY ");
        var filtered = order < 0 ? rest : rest.Substring(0, order);
        var ordering = order < 0 ? "" : rest.Substring(order);
        var picked = (TopLevel(filtered, " WHERE ") >= 0 ? filtered + " AND " : filtered + " WHERE ") + "t.ROWID IN (SELECT CHARTOROWID(COLUMN_VALUE) FROM TABLE(pragmatic_ids))";
        return "DECLARE pragmatic_ids SYS.ODCIVARCHAR2LIST; pragmatic_c0 SYS_REFCURSOR; pragmatic_c1 SYS_REFCURSOR; BEGIN"
            + $" OPEN pragmatic_c0 FOR SELECT ROWIDTOCHAR(t.ROWID){rest} {page.Groups["lock"].Value};"
            + $" FETCH pragmatic_c0 BULK COLLECT INTO pragmatic_ids LIMIT {page.Groups["take"].Value}; CLOSE pragmatic_c0;"
            + $" OPEN pragmatic_c1 FOR {select.Substring(0, from)}{picked}{ordering}; DBMS_SQL.RETURN_RESULT(pragmatic_c1); END;";
    }

    /// <summary>The first <paramref name="token" /> outside parentheses and quotes; -1 when there is none.</summary>
    private static int TopLevel(string sql, string token)
    {
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '\'')
                quoted = !quoted;
            if (quoted)
                continue;
            if (c == '(')
                depth++;
            else if (c == ')')
                depth--;
            else if (depth == 0 && string.CompareOrdinal(sql, i, token, 0, token.Length) == 0)
                return i;
        }

        return -1;
    }

    private static string Block(string sql)
    {
        var declarations = new StringBuilder("DECLARE ").Append(Count).Append(" NUMBER := 0; ").Append(Total).Append(" NUMBER := 0;");
        var body = new StringBuilder(" BEGIN");
        var cursors = 0;
        var returned = new List<string>();
        var kept = 0;
        foreach (var raw in sql.Split("; "))
        {
            var statement = raw.Trim().TrimEnd(';');
            if (statement.Length == 0)
                continue;
            if (statement == "SELECT ROW_COUNT()")
            {
                Open($"SELECT {Count} FROM dual");
                continue;
            }

            if (statement == "SELECT RETURNED")
            {
                if (returned.Count == 0)
                    Open("SELECT NULL FROM dual WHERE 1 = 0");
                else
                    Open("SELECT v FROM (" + string.Join(" UNION ALL ", returned.Select((v, i) => $"SELECT {v} AS v, {i.ToString(CultureInfo.InvariantCulture)} AS o FROM dual")) + ") ORDER BY o");
                returned.Clear();
                continue;
            }

            var head = statement.StartsWith("/*", StringComparison.Ordinal) && statement.IndexOf("*/", StringComparison.Ordinal) is var end and > 0 ? statement.Substring(end + 2).TrimStart() : statement; // the origin comment of an annotating session
            if (head.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase) || head.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase) || head.StartsWith("(SELECT ", StringComparison.OrdinalIgnoreCase))
            {
                Open(statement.Replace("ROW_COUNT()", Count, StringComparison.Ordinal));
                continue;
            }

            // DML: executed, its count kept; a RETURNING "Col" keeps the value in a variable of its own.
            var match = Returning().Match(statement);
            if (match.Success)
            {
                var variable = "pragmatic_r" + (kept++).ToString(CultureInfo.InvariantCulture);
                declarations.Append(' ').Append(variable).Append(" NUMBER;");
                statement = statement.Substring(0, match.Index) + " RETURNING " + match.Groups[1].Value + " INTO " + variable;
                returned.Add(variable);
            }

            body.Append(' ').Append(statement).Append("; ").Append(Count).Append(" := SQL%ROWCOUNT; ").Append(Total).Append(" := ").Append(Total).Append(" + ").Append(Count).Append(';');
        }

        if (cursors == 0)
            Open($"SELECT {Total} FROM dual"); // DML only: the rows they touched, as ExecuteNonQuery would count them (SqlSession.RunAsync reads it)
        return declarations.Append(body).Append(" END;").ToString();

        void Open(string select)
        {
            var cursor = "pragmatic_c" + (cursors++).ToString(CultureInfo.InvariantCulture);
            declarations.Append(' ').Append(cursor).Append(" SYS_REFCURSOR;");
            body.Append(" OPEN ").Append(cursor).Append(" FOR ").Append(select).Append("; DBMS_SQL.RETURN_RESULT(").Append(cursor).Append(");");
        }
    }
}
