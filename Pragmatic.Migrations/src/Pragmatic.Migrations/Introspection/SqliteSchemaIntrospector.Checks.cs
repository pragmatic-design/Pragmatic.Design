using System.Data.Common;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Introspection;

public sealed partial class SqliteSchemaIntrospector
{
    /// <summary>
    ///     Every named check constraint, recovered from the <c>CREATE TABLE</c> text SQLite keeps in
    ///     <c>sqlite_master</c>, since no PRAGMA reports one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it the diff, which compares checks by name, finds every declared check missing on
    ///     every run. An unnamed <c>CHECK (…)</c> is left out: there is no name to compare it by, and the
    ///     generator names every check it writes.
    /// </remarks>
    private static async Task<Dictionary<string, List<CheckConstraintSchema>>> ReadAllCheckConstraintsAsync(
        DbConnection connection, HashSet<string> wanted, CancellationToken ct)
    {
        var byTable = new Dictionary<string, List<CheckConstraintSchema>>(StringComparer.Ordinal);

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = $"SELECT m.name, m.sql FROM sqlite_master m WHERE {UserTables} AND m.sql IS NOT NULL";

            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var table = reader.GetString(0);
                    if (!wanted.Contains(table)) continue;

                    foreach (var check in ExtractCheckConstraints(reader.GetString(1)))
                        Add(byTable, table, check);
                }
            }
        }

        return byTable;
    }

    /// <summary>
    ///     The <c>CONSTRAINT name CHECK (expression)</c> clauses of a <c>CREATE TABLE</c> statement, at the
    ///     level of its column list, the name unquoted and the expression as written.
    /// </summary>
    internal static List<CheckConstraintSchema> ExtractCheckConstraints(string createTableSql)
    {
        var checks = new List<CheckConstraintSchema>();
        var sql = createTableSql;
        var depth = 0;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c is '\'' or '"' or '`' or '[') { i = ClosingQuote(sql, i); continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth--; continue; }
            if (depth != 1 || !KeywordAt(sql, i, "CONSTRAINT")) continue;

            var nameStart = SkipSpace(sql, i + "CONSTRAINT".Length);
            if (nameStart >= sql.Length) break;
            var nameEnd = sql[nameStart] is '"' or '`' or '['
                ? ClosingQuote(sql, nameStart) + 1
                : BareEnd(sql, nameStart);
            var check = SkipSpace(sql, nameEnd);
            var open = SkipSpace(sql, check + "CHECK".Length);
            if (!KeywordAt(sql, check, "CHECK") || open >= sql.Length || sql[open] != '(')
            {
                i = nameEnd - 1; // a named key or reference: not a check
                continue;
            }

            var close = ClosingParenthesis(sql, open);
            checks.Add(new CheckConstraintSchema(
                Unquote(sql.Substring(nameStart, nameEnd - nameStart)),
                sql.Substring(open + 1, close - open - 1).Trim()));
            i = close;
        }

        return checks;
    }

    private static bool KeywordAt(string sql, int at, string keyword) =>
        at + keyword.Length <= sql.Length
        && sql.AsSpan(at, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase)
        && (at == 0 || !IsIdentifierChar(sql[at - 1]))
        && (at + keyword.Length == sql.Length || !IsIdentifierChar(sql[at + keyword.Length]));

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static int SkipSpace(string sql, int at)
    {
        while (at < sql.Length && char.IsWhiteSpace(sql[at])) at++;
        return at;
    }

    private static int BareEnd(string sql, int at)
    {
        while (at < sql.Length && IsIdentifierChar(sql[at])) at++;
        return at;
    }

    /// <summary>The index of the quote closing the one at <paramref name="open" /> (a doubled quote is part of the text; a bracket has no escape).</summary>
    private static int ClosingQuote(string sql, int open)
    {
        var close = sql[open] == '[' ? ']' : sql[open];
        for (var i = open + 1; i < sql.Length; i++)
        {
            if (sql[i] != close) continue;
            if (close != ']' && i + 1 < sql.Length && sql[i + 1] == close) { i++; continue; }
            return i;
        }

        return sql.Length - 1;
    }

    /// <summary>The index of the parenthesis closing the one at <paramref name="open" />, quotes skipped.</summary>
    private static int ClosingParenthesis(string sql, int open)
    {
        var depth = 0;
        for (var i = open; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c is '\'' or '"' or '`' or '[') { i = ClosingQuote(sql, i); continue; }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }

        return sql.Length - 1;
    }

    private static string Unquote(string name) => name.Length < 2 || name[0] is not ('"' or '`' or '[') ? name
        : name[0] == '[' ? name.Substring(1, name.Length - 2)
        : name.Substring(1, name.Length - 2).Replace(new string(name[0], 2), name[0].ToString());
}
