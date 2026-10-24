using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Schema;

/// <summary>
///     Multi-tenancy by schema: the statements the generator and the writers build name tables bare (<c>FROM "Orders"</c>,
///     <c>[Orders]</c> on SQL Server); a session with a <see cref="SqlSession.Schema" /> runs them with every table qualified
///     (<c>FROM "tenant_a"."Orders"</c>). The text is rewritten, never translated again: a plan keeps one text per schema it has
///     run on, a session's prepared commands are already on its schema. A table is what follows FROM, JOIN, INTO, UPDATE, TABLE
///     (DDL) and REFERENCES as a quoted identifier; an identifier already qualified (<c>"x"."T"</c>) is left alone, as is anything
///     unquoted (a CTE's name, a function). On SQLite an attached database stands for a schema (<c>ATTACH DATABASE … AS "tenant_a"</c>).
/// </summary>
public static class SqlTenantSchema
{
    private static readonly Regex Quoted = new(@"\b(FROM|JOIN|INTO|UPDATE|TABLE|REFERENCES)(\s+(?:IF\s+(?:NOT\s+)?EXISTS\s+)?)""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex QuotedSqlite = new(@"\b(FROM|JOIN|INTO|UPDATE|TABLE)(\s+(?:IF\s+(?:NOT\s+)?EXISTS\s+)?)""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase); // a foreign key refers to the same database: REFERENCES takes no schema
    private static readonly Regex Bracketed = new(@"\b(FROM|JOIN|INTO|UPDATE|TABLE|REFERENCES)(\s+(?:IF\s+(?:NOT\s+)?EXISTS\s+)?)\[([^\]]+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The statement with every table it names qualified by <paramref name="schema" />, in the dialect's quoting; the text itself when the schema is null or empty.</summary>
    public static string Qualify(string sql, SqlDialect dialect, string? schema)
    {
        if (string.IsNullOrEmpty(schema))
            return sql;
        return dialect switch
        {
            SqlDialect.SqlServer => Bracketed.Replace(sql, m => $"{m.Groups[1].Value}{m.Groups[2].Value}[{schema}].[{m.Groups[3].Value}]"),
            SqlDialect.Sqlite => QuotedSqlite.Replace(sql, m => $"{m.Groups[1].Value}{m.Groups[2].Value}\"{schema}\".\"{m.Groups[3].Value}\""),
            _ => Quoted.Replace(sql, m => $"{m.Groups[1].Value}{m.Groups[2].Value}\"{schema}\".\"{m.Groups[3].Value}\""),
        };
    }

    /// <summary>A table name qualified and quoted for the dialect (<c>"tenant_a"."Orders"</c>, <c>[tenant_a].[Orders]</c>); bare quoted without a schema.</summary>
    public static string Table(SqlDialect dialect, string? schema, string table)
        => string.IsNullOrEmpty(schema) ? SqlDialects.Quote(dialect, table) : SqlDialects.Quote(dialect, schema!) + "." + SqlDialects.Quote(dialect, table);
}
