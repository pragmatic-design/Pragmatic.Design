using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     The statements on a many-to-many join table (<c>[ManyToMany]</c>: the two sides' key columns, one per key column of a composite key, no
///     entity, no writer of its own), as items of a <see cref="SqlWriteBatch" />: one link inserted or deleted, a link inserted where missing, the
///     links of a parent to rows not in a list deleted, the links of a set of parents deleted. The text is built per call (the table is not a
///     plan's), quoted for the dialect. A side's key is its values in key order (<see cref="SqlSession.KeyValues" />).
/// </summary>
internal static class SqlLinkItems
{
    /// <summary>INSERT of one link; the join table's key refuses a duplicate.</summary>
    public static SqlWriteItem Insert(SqlCollection link, SqlParamKind[] parentKinds, object?[] parentKey, SqlParamKind[] childKinds, object?[] childKey)
        => new(link.LinkTable!, -1, [.. parentKinds, .. childKinds], d => $"INSERT INTO {Q(d, link.LinkTable!)} ({Columns(d, link)}) VALUES ({Parameters(0, parentKey.Length + childKey.Length)})", Bind(parentKey, childKey), expected: 1);

    /// <summary>DELETE of one link: one row, or a conflict (the link was not there).</summary>
    public static SqlWriteItem Delete(SqlCollection link, SqlParamKind[] parentKinds, object?[] parentKey, SqlParamKind[] childKinds, object?[] childKey)
        => new(link.LinkTable!, -1, [.. parentKinds, .. childKinds], d => $"DELETE FROM {Q(d, link.LinkTable!)} WHERE {Equalities(d, link)}", Bind(parentKey, childKey), expected: 1);

    /// <summary>INSERT of a link unless it is there: INSERT OR IGNORE, ON CONFLICT DO NOTHING, IF NOT EXISTS, by dialect.</summary>
    public static SqlWriteItem Ensure(SqlCollection link, SqlParamKind[] parentKinds, object?[] parentKey, SqlParamKind[] childKinds, object?[] childKey)
        => new(link.LinkTable!, -1, [.. parentKinds, .. childKinds], d =>
        {
            var table = Q(d, link.LinkTable!);
            var columns = Columns(d, link);
            var values = Parameters(0, parentKey.Length + childKey.Length);
            var equal = Equalities(d, link);
            return d switch
            {
                SqlDialect.Sqlite => $"INSERT OR IGNORE INTO {table} ({columns}) VALUES ({values})",
                SqlDialect.PostgreSql => $"INSERT INTO {table} ({columns}) VALUES ({values}) ON CONFLICT DO NOTHING",
                SqlDialect.MySql => $"INSERT IGNORE INTO {table} ({columns}) VALUES ({values})",
                SqlDialect.Oracle => $"INSERT INTO {table} ({columns}) SELECT {values} FROM dual WHERE NOT EXISTS (SELECT 1 FROM {table} WHERE {equal})",
                _ => $"IF NOT EXISTS (SELECT 1 FROM {table} WHERE {equal}) INSERT INTO {table} ({columns}) VALUES ({values})",
            };
        }, Bind(parentKey, childKey), expected: -1);

    /// <summary>DELETE of the parent's links to rows whose key is not among <paramref name="keptKeys" /> (every link of the parent when the list is empty).</summary>
    public static SqlWriteItem Orphans(SqlCollection link, SqlParamKind[] parentKinds, object?[] parentKey, SqlParamKind[] childKinds, IReadOnlyList<object?[]> keptKeys)
    {
        var kinds = new List<SqlParamKind>(parentKinds);
        foreach (var _ in keptKeys)
            kinds.AddRange(childKinds);
        return new SqlWriteItem(link.LinkTable!, -1, kinds.ToArray(), d =>
        {
            var parent = link.ForeignKeyColumns;
            var child = link.ChildColumns;
            var sql = new StringBuilder("DELETE FROM ").Append(Q(d, link.LinkTable!)).Append(" WHERE ");
            for (var k = 0; k < parent.Length; k++)
                sql.Append(k > 0 ? " AND " : "").Append(Q(d, parent[k])).Append(" = @p").Append(k.ToString(CultureInfo.InvariantCulture));
            if (keptKeys.Count > 0)
            {
                // The kept rows' keys: NOT IN for a single column, a negated disjunction of conjunctions for a composite key (SQL Server has no row-value IN).
                var at = parent.Length;
                sql.Append(" AND NOT (");
                for (var i = 0; i < keptKeys.Count; i++)
                {
                    sql.Append(i > 0 ? " OR (" : "(");
                    for (var c = 0; c < child.Length; c++)
                        sql.Append(c > 0 ? " AND " : "").Append(Q(d, child[c])).Append(" = @p").Append((at++).ToString(CultureInfo.InvariantCulture));
                    sql.Append(')');
                }

                sql.Append(')');
            }

            return sql.ToString();
        }, (p, o, d) =>
        {
            var at = o;
            foreach (var value in parentKey)
                p[at++].Value = SqlValue.ToDatabase(d, value);
            foreach (var kept in keptKeys)
            {
                foreach (var value in kept)
                    p[at++].Value = SqlValue.ToDatabase(d, value);
            }
        }, expected: -1);
    }

    /// <summary>DELETE of the links a condition on the join table selects, with the root's key as its parameters (<c>@p0</c> on).</summary>
    public static SqlWriteItem DeleteWhere(SqlCollection link, Func<SqlDialect, string> where, SqlParamKind[] parentKinds, object?[] parentKey)
        => new(link.LinkTable!, -1, parentKinds, d => $"DELETE FROM {Q(d, link.LinkTable!)} WHERE {where(d)}", (p, o, d) =>
        {
            for (var k = 0; k < parentKey.Length; k++)
                p[o + k].Value = SqlValue.ToDatabase(d, parentKey[k]);
        }, expected: -1);

    /// <summary>The join table's columns, the parent's then the child's.</summary>
    private static string Columns(SqlDialect dialect, SqlCollection link) => string.Join(", ", link.ForeignKeyColumns.Concat(link.ChildColumns).Select(c => Q(dialect, c)));

    /// <summary>Each join-table column equal to its parameter, the parent's from <c>@p0</c>, the child's after them.</summary>
    private static string Equalities(SqlDialect dialect, SqlCollection link)
        => string.Join(" AND ", link.ForeignKeyColumns.Concat(link.ChildColumns).Select((c, i) => $"{Q(dialect, c)} = @p{i.ToString(CultureInfo.InvariantCulture)}"));

    private static string Parameters(int from, int count) => string.Join(", ", Enumerable.Range(from, count).Select(i => "@p" + i.ToString(CultureInfo.InvariantCulture)));

    private static Action<System.Data.Common.DbParameterCollection, int, SqlDialect> Bind(object?[] parentKey, object?[] childKey)
        => (p, o, d) =>
        {
            var at = o;
            foreach (var value in parentKey)
                p[at++].Value = SqlValue.ToDatabase(d, value);
            foreach (var value in childKey)
                p[at++].Value = SqlValue.ToDatabase(d, value);
        };

    private static string Q(SqlDialect dialect, string name) => SqlDialects.Quote(dialect, name);
}
