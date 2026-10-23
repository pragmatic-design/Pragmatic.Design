using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>A column to order by, compiled for every dialect on the row alias <c>t</c>: a key of <see cref="SqlOrdering{T}" />.</summary>
public sealed class SqlOrderKey(string sqlite, string postgreSql, string sqlServer, string mySql, string oracle)
{
    private readonly string[] _texts = [sqlite, postgreSql, sqlServer, mySql, oracle];

    public string Text(SqlDialect dialect) => _texts[(int)dialect];
}

/// <summary>
///     An ordering over <typeparamref name="T" />'s rows chosen at runtime from compiled keys (a declared query's sorts, the request picking the
///     columns and directions): rendered in the statement's ORDER BY where the chain says <c>OrderedBy</c>. Without a key it orders by the
///     fallback (the entity's key), so a page is the same page on every execution.
/// </summary>
public sealed class SqlOrdering<T> : SqlComposed
{
    private readonly SqlOrderKey _fallback;
    private readonly (SqlOrderKey Key, bool Descending)[] _keys;

    private SqlOrdering(SqlOrderKey fallback, (SqlOrderKey, bool)[] keys)
    {
        _fallback = fallback;
        _keys = keys;
    }

    /// <summary>No key yet: the rows in <paramref name="fallback" />'s order until one is added.</summary>
    public static SqlOrdering<T> By(SqlOrderKey fallback) => new(fallback, []);

    /// <summary>This ordering, then <paramref name="key" />.</summary>
    public SqlOrdering<T> Then(SqlOrderKey key, bool descending) => new(_fallback, [.. _keys, (key, descending)]);

    public override void Render(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter)
    {
        if (_keys.Length == 0)
        {
            text.Append(OnAlias(_fallback.Text(dialect), alias));
            return;
        }

        for (var i = 0; i < _keys.Length; i++)
            text.Append(i == 0 ? "" : ", ").Append(OnAlias(_keys[i].Key.Text(dialect), alias)).Append(_keys[i].Descending ? " DESC" : " ASC");
    }

    private static string OnAlias(string column, string alias)
        => alias == "t" ? column : column.Replace("t.\"", alias + ".\"", StringComparison.Ordinal).Replace("t.[", alias + ".[", StringComparison.Ordinal);
}
