using System.Linq;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Schema;

/// <summary>
///     <c>Sql.FullText</c> and <c>Sql.FullTextRank</c> on a <c>[FullText]</c> column: the engines' own full-text search where the column's index is
///     there (<c>SqlFullText.CreateSql</c>). Arguments: the column, the query, the column's table, its name, the table's key column. Every engine
///     wants every word of the query, as PostgreSQL's <c>plainto_tsquery</c> does: the query travels as a <c>SqlFullTextQuery</c> and is bound as the
///     engine's condition (<see cref="FullTextCondition" />). SQLite: the FTS5 table <c>{Table}_{Column}_fts</c> (external content, kept by
///     triggers), ranked by bm25; SQL Server: <c>CONTAINS</c>, ranked by <c>CONTAINSTABLE</c>; MySQL: <c>MATCH … AGAINST</c> in boolean mode, its
///     relevance the rank; Oracle: <c>CONTAINS</c>, ranked by <c>SCORE</c>; PostgreSQL as the plain entries (tsvector). SQL Server's, MySQL's and
///     Oracle's indexes see committed rows (Oracle's synchronised on commit, SQL Server's populated after it).
/// </summary>
internal static partial class SqlCatalog
{
    private static readonly Regex Words = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    private static readonly Entry[] FullTextEntries =
    [
        new("FullTextNative", 5, (d, a) => d switch
        {
            SqlDialect.Sqlite => $"({Alias(a[0])}.rowid IN (SELECT rowid FROM {Fts(a)} WHERE {Fts(a)} MATCH {Query(d, a[1])}))",
            SqlDialect.SqlServer => $"CONTAINS({a[0]}, {Query(d, a[1])})",
            SqlDialect.MySql => $"(MATCH({a[0]}) AGAINST ({Query(d, a[1])} IN BOOLEAN MODE) > 0)",
            SqlDialect.Oracle => $"(CONTAINS({a[0]}, {Query(d, a[1])}) > 0)",
            _ => Find("FullText", 2)!.Render(d, [a[0], a[1]]),
        }, isPredicate: true),
        new("FullTextRankNative", 5, (d, a) => d switch
        {
            SqlDialect.Sqlite => $"COALESCE((SELECT -bm25({Fts(a)}) FROM {Fts(a)} WHERE {Fts(a)} MATCH {Query(d, a[1])} AND rowid = {Alias(a[0])}.rowid), 0)", // bm25: lower is better
            SqlDialect.SqlServer => $"COALESCE((SELECT CAST(ft.\"RANK\" AS float) FROM CONTAINSTABLE(\"{a[2]}\", \"{a[3]}\", {Query(d, a[1])}) ft WHERE ft.\"KEY\" = {Alias(a[0])}.\"{a[4]}\"), 0)",
            SqlDialect.MySql => $"MATCH({a[0]}) AGAINST ({Query(d, a[1])} IN BOOLEAN MODE)",
            SqlDialect.Oracle => $"COALESCE((SELECT SCORE(1) FROM \"{a[2]}\" ft WHERE ft.\"{a[4]}\" = {Alias(a[0])}.\"{a[4]}\" AND CONTAINS(ft.\"{a[3]}\", {Query(d, a[1])}, 1) > 0), 0)", // SCORE belongs to the CONTAINS of its own query
            _ => Find("FullTextRank", 2)!.Render(d, [a[0], a[1]]),
        }),
    ];

    /// <summary>The FTS5 table of a column: <c>"{Table}_{Column}_fts"</c>.</summary>
    internal static string FtsTable(string table, string column) => table + "_" + column + "_fts";

    /// <summary>
    ///     A full-text query as the engine's condition, every word required (the words: the runs of letters and digits, the engines' operators inert):
    ///     SQL Server <c>"a" AND "b"</c>, Oracle <c>{a} AND {b}</c>, SQLite <c>"a" "b"</c>, MySQL's boolean mode <c>+a +b</c>; PostgreSQL the text
    ///     itself (plainto_tsquery reads it). <paramref name="quoted" /> false for a literal in a statement on SQL Server, whose double quotes are
    ///     identifiers': the words bare, the condition's keywords left out.
    /// </summary>
    internal static string FullTextCondition(SqlDialect dialect, string query, bool quoted = true)
    {
        if (dialect == SqlDialect.PostgreSql)
            return query;
        var words = Words.Split(query).Where(w => w.Length > 0).ToArray();
        if (words.Length == 0)
            throw new System.ArgumentException($"The full-text query '{query}' has no word to search for.", nameof(query));
        return dialect switch
        {
            SqlDialect.SqlServer when quoted => string.Join(" AND ", words.Select(w => "\"" + w + "\"")),
            SqlDialect.SqlServer => string.Join(" AND ", words.Where(w => w.ToUpperInvariant() is not ("AND" or "OR" or "NOT" or "NEAR"))),
            SqlDialect.Oracle => string.Join(" AND ", words.Select(w => "{" + w + "}")),
            SqlDialect.MySql => string.Join(" ", words.Select(w => "+" + w)),
            _ => string.Join(" ", words.Select(w => "\"" + w + "\"")),
        };
    }

    /// <summary>The query argument: a parameter as it is (its <c>SqlFullTextQuery</c> value becomes the condition when bound), a literal turned into it here.</summary>
    private static string Query(SqlDialect dialect, string argument)
        => argument.Length > 1 && argument[0] == '\'' && argument[argument.Length - 1] == '\''
            ? "'" + FullTextCondition(dialect, argument.Substring(1, argument.Length - 2).Replace("''", "'"), quoted: dialect != SqlDialect.SqlServer).Replace("'", "''") + "'"
            : argument;

    private static string Fts(string[] a) => "\"" + FtsTable(a[2], a[3]) + "\"";

    /// <summary>The row's alias in a column fragment (<c>t."Title"</c> → <c>t</c>).</summary>
    private static string Alias(string column) => column.Substring(0, column.IndexOf('.'));
}
