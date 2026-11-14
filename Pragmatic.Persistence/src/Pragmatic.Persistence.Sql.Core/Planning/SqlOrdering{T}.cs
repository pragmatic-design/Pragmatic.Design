using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     An ordering over <typeparamref name="T" />'s rows chosen at runtime from compiled keys (a declared query's sorts, the request picking the
///     columns and directions): rendered in the statement's ORDER BY where the chain says <c>OrderedBy</c>. Without a key it orders by the
///     fallback (the entity's key), so a page is the same page on every execution.
/// </summary>
public sealed class SqlOrdering<T> : SqlComposed
{
    private readonly SqlOrderKey _fallback;
    private readonly (SqlOrderKey Key, bool Descending, bool? NullsLast)[] _keys;

    private SqlOrdering(SqlOrderKey fallback, (SqlOrderKey, bool, bool?)[] keys)
    {
        _fallback = fallback;
        _keys = keys;
    }

    /// <summary>No key yet: the rows in <paramref name="fallback" />'s order until one is added.</summary>
    public static SqlOrdering<T> By(SqlOrderKey fallback) => new(fallback, []);

    /// <summary>This ordering, then <paramref name="key" />, null keys where the database puts them.</summary>
    public SqlOrdering<T> Then(SqlOrderKey key, bool descending) => new(_fallback, [.. _keys, (key, descending, null)]);

    /// <summary>
    ///     This ordering, then <paramref name="key" />, the rows whose key is null last (<paramref name="nullsLast" />) or first, whatever the
    ///     direction (<c>[Sort(Nulls = …)]</c>, #733).
    /// </summary>
    public SqlOrdering<T> Then(SqlOrderKey key, bool descending, bool nullsLast) => new(_fallback, [.. _keys, (key, descending, nullsLast)]);

    public override void Render(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter)
    {
        if (_keys.Length == 0)
        {
            text.Append(OnAlias(_fallback.Text(dialect), alias));
            return;
        }

        for (var i = 0; i < _keys.Length; i++)
        {
            var column = OnAlias(_keys[i].Key.Text(dialect), alias);
            text.Append(i == 0 ? "" : ", ");
            if (_keys[i].NullsLast is { } last && !HasNullsClause(dialect))
            {
                // No NULLS FIRST/LAST in T-SQL and MySQL: the rows ordered by their null-ness first, then by the column.
                text.Append("CASE WHEN ").Append(column).Append(last ? " IS NULL THEN 1 ELSE 0 END, " : " IS NULL THEN 0 ELSE 1 END, ");
            }

            text.Append(column).Append(_keys[i].Descending ? " DESC" : " ASC");
            if (_keys[i].NullsLast is { } nullsLast && HasNullsClause(dialect))
                text.Append(nullsLast ? " NULLS LAST" : " NULLS FIRST");
        }
    }

    /// <summary>Whether the dialect writes <c>NULLS FIRST</c>/<c>NULLS LAST</c> after a sort key: PostgreSQL, Oracle and SQLite (3.30 and later).</summary>
    private static bool HasNullsClause(SqlDialect dialect)
        => dialect is SqlDialect.PostgreSql or SqlDialect.Oracle or SqlDialect.Sqlite;

    private static string OnAlias(string column, string alias)
        => alias == "t" ? column : column.Replace("t.\"", alias + ".\"", StringComparison.Ordinal).Replace("t.[", alias + ".[", StringComparison.Ordinal);
}
