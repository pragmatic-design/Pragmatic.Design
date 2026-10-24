using System.Data.Common;
using System.Linq.Expressions;
using System.Text;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>
///     Include on the runtime path, through the registered maps: the same joins, select columns and child
///     queries the generator emits for the typed chain, built from the member path of the Include lambda.
///     Nothing is compiled: the navigations' generated readers and setters do the hydration.
/// </summary>
internal static partial class DynamicIncludes
{
    /// <summary>
    ///     Adds an Include lambda's path to the tree: member accesses (<c>l => l.Order.Customer</c>, <c>o => o.Lines</c>), <see cref="SqlPath" />
    ///     <c>Then</c> steps below a collection or a navigation (<c>c => c.Orders.Then(o => o.Lines)</c>), and LINQ's <c>Where</c>, <c>OrderBy</c>/
    ///     <c>ThenBy</c> and <c>Take</c> on a collection as its filter, ordering and Take per parent (<c>c => c.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).Take(3)</c>).
    /// </summary>
    public static void Add(List<RuntimeInclude> roots, SqlEntityMap map, LambdaExpression path)
    {
        var names = new List<PathPart>();
        if (!Names(path.Body, path.Parameters[0], names) || names.Count == 0)
            throw new NotSupportedException($"'{path}' is not a navigation path on the lambda parameter: Include takes o => o.Customer, l => l.Order.Customer, o => o.Lines, c => c.Orders.Then(o => o.Lines) or c => c.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).Take(3).");

        var level = roots;
        var current = map;
        foreach (var part in names)
        {
            var (name, filter) = (part.Name, part.Filter);
            RuntimeInclude node;
            if (current.Navigation(name) is { } navigation)
            {
                if (filter is not null || part.Orderings.Count > 0 || part.Take is not null)
                    throw new NotSupportedException($"'{name}' is a to-one navigation: Where, OrderBy and Take work on a collection.");
                var target = SqlEntityMap.For(navigation.Target) ?? throw new InvalidOperationException($"No map is registered for {navigation.Target.Name}.");
                node = level.FirstOrDefault(n => n.Property == name) ?? Insert(level, new RuntimeInclude(name, target, navigation, null));
            }
            else if (current.Collection(name) is { } collection)
            {
                SqlKeyMatch.Check(current, collection); // a composite parent: one foreign key per key column
                var child = SqlEntityMap.For(collection.Child) ?? throw new InvalidOperationException($"No map is registered for {collection.Child.Name}.");
                if (filter is not null && HasCaptures(filter.Body))
                    throw new NotSupportedException($"'{filter}' reads a value of the caller: an Include filter compares columns with constants only (the plan is cached by the chain's shape, the child query shared by every call); filter the parent chain, or the loaded rows.");
                foreach (var (key, _) in part.Orderings)
                {
                    if (HasCaptures(key.Body))
                        throw new NotSupportedException($"'{key}' in the Include ordering reads a value of the caller: an Include orders the children by their own columns.");
                }

                if (part.Take is { } take)
                {
                    if (take <= 0)
                        throw new NotSupportedException("Take in an Include takes a positive constant.");
                    if (part.Orderings.Count == 0)
                        throw new NotSupportedException($"'{name}' is taken per parent without an order: Take follows OrderBy in an Include (c.Orders.OrderBy(o => o.CreatedAt).Take(3)).");
                    if (collection.IsLink)
                        throw new NotSupportedException($"'{name}' is a many-to-many side: a Take per parent goes through the join table, which is not written inside the window; load the rows and take in memory.");
                }

                node = level.FirstOrDefault(n => n.Property == name) ?? Insert(level, new RuntimeInclude(name, child, null, collection, filter, part.Orderings, part.Take));
            }
            else
            {
                throw new NotSupportedException($"'{name}' is not a navigation or a collection of {current.Type.Name}.");
            }

            level = node.Children;
            current = node.Target;
        }
    }

    /// <summary>The joins of the to-one nodes (depth first, parent before child) and the columns they add to the select list: the generator's aliases (<c>j_Order</c>, <c>j_Order_Customer</c>).</summary>
    public static void Joins(List<RuntimeInclude> nodes, string parentAlias, List<string> joins, List<string> columns, Func<SqlEntityMap, string, string> filter)
    {
        foreach (var node in nodes.Where(n => !n.IsCollection))
        {
            var alias = parentAlias == "t" ? "j_" + node.Property : parentAlias + "_" + node.Property;
            var join = node.Navigation!.Join(node.Target, parentAlias, alias);
            if (!joins.Exists(j => j == join || j.StartsWith(join + " AND ", StringComparison.Ordinal)))
            {
                var at = joins.Count;
                joins.Add(join);
                joins[at] = join + filter(node.Target, alias); // a target the caller cannot see: no row (required), null (optional)
            }

            foreach (var column in node.Target.Columns)
                columns.Add(column.Select(alias));
            Joins(node.Children, alias, joins, columns, filter);
        }
    }

    /// <summary>The paths of the tree's nodes, for the plan cache's key: two chains with the same SQL and different includes below a collection are two plans.</summary>
    public static string Signature(List<RuntimeInclude> nodes)
    {
        var text = new StringBuilder();
        Signature(nodes, "", text);
        return text.ToString();
    }

    private static void Signature(List<RuntimeInclude> nodes, string prefix, StringBuilder text)
    {
        foreach (var node in nodes)
        {
            text.Append(prefix).Append(node.Property);
            if (node.Filter is not null)
                text.Append('[').Append(node.Filter).Append(']'); // constants only (see Add): the text is the filter's identity
            foreach (var (key, descending) in node.Orderings)
                text.Append('<').Append(key).Append(descending ? " desc>" : ">");
            if (node.Take is { } take)
                text.Append('#').Append(take);
            text.Append(';');
            Signature(node.Children, prefix + node.Property + ".", text);
        }
    }

    /// <summary>The entity read, then each to-one object read from its columns (at the ordinals the select list gave them) and set on its owner.</summary>
    public static Func<DbDataReader, SqlDialect, T> Reader<T>(Func<DbDataReader, SqlDialect, T> read, List<RuntimeInclude> nodes, int firstOrdinal)
    {
        var ordinal = firstOrdinal;
        var setters = Setters(nodes, ref ordinal);
        if (setters.Length == 0)
            return read;
        return (r, d) =>
        {
            var e = read(r, d)!;
            foreach (var setter in setters)
                setter(r, d, e);
            return e;
        };
    }

    private static Action<DbDataReader, SqlDialect, object>[] Setters(List<RuntimeInclude> nodes, ref int ordinal)
    {
        var setters = new List<Action<DbDataReader, SqlDialect, object>>();
        foreach (var node in nodes.Where(n => !n.IsCollection))
        {
            var start = ordinal;
            ordinal += node.Target.Columns.Length;
            var keyOrdinal = start + Math.Max(0, Array.FindIndex(node.Target.Columns, c => c.Column == node.Target.KeyColumn));
            var navigation = node.Navigation!;
            var below = Setters(node.Children, ref ordinal);
            setters.Add((r, d, owner) =>
            {
                object? value = null;
                if (!r.IsDBNull(keyOrdinal))
                {
                    value = navigation.ReadAt(r, start, d);
                    foreach (var setter in below)
                        setter(r, d, value);
                }

                navigation.Set(owner, value);
            });
        }

        return setters.ToArray();
    }

    // Collection includes: one companion statement per collection, in the same command as the rows' own, selecting the children
    // whose foreign key is in the parent statement's keys (the parent statement as a subquery, paging included). The loader reads the
    // result sets that follow the rows' own, in the order the statements were built: one round trip whatever the depth.

    /// <summary>One companion statement: the collection node it loads and the reader of its rows (the child with its to-one includes).</summary>
    private sealed class Companion(RuntimeInclude node, Func<DbDataReader, SqlDialect, object> read, Func<DbDataReader, SqlDialect, object?>? linkKey = null)
    {
        public RuntimeInclude Node { get; } = node;

        public Func<DbDataReader, SqlDialect, object> Read { get; } = read;

        /// <summary>Through a join table: the parent's key read from the row's last column (the child carries none).</summary>
        public Func<DbDataReader, SqlDialect, object?>? LinkKey { get; } = linkKey;
    }

    private sealed class Cursor
    {
        public int Index;
    }

    /// <summary>
    ///     The companion statements of the collection nodes under <paramref name="nodes" /> (appended to <paramref name="statements" />,
    ///     depth first, their values to the shared lists) and the loader that reads their result sets in that order; null when the
    ///     tree has no collection. <paramref name="renderParent" /> renders the parent statement with a given select list.
    /// </summary>
    public static SqlLoad<T>? Loader<T>(List<RuntimeInclude> nodes, SqlEntityMap map, SqlDialect dialect, Func<string, string> renderParent, List<string> statements, List<object?> values, List<SqlParamKind> kinds, SqlCapture capture)
    {
        if (!nodes.Any(n => n.LoadsAnything))
            return null;
        var companions = new List<Companion>();
        Build(nodes, dialect, renderParent, "t", map, companions, statements, values, kinds, capture);
        return async (reader, dialect, rows, ct) =>
        {
            var parents = new List<object>(rows.Count);
            foreach (var row in rows)
                parents.Add(row!);
            await LoadAsync(reader, dialect, parents, map, nodes, companions, new Cursor(), ct).ConfigureAwait(false);
        };
    }

    private static void Build(List<RuntimeInclude> nodes, SqlDialect dialect, Func<string, string> renderParent, string parentAlias, SqlEntityMap keyOwner, List<Companion> companions, List<string> statements, List<object?> values, List<SqlParamKind> kinds, SqlCapture capture)
    {
        foreach (var node in nodes)
        {
            if (node.IsCollection)
            {
                var child = node.Target;
                var joins = new List<string>();
                var innerAliases = new HashSet<string>(StringComparer.Ordinal);
                var predicates = new List<string>();
                if (node.Filter is { } filter)
                    predicates.Add(new ExpressionTranslator(child, filter.Parameters[0], dialect, values, kinds, joins, capture, innerAliases).Predicate(filter.Body));
                foreach (var rule in child.Rules)
                {
                    var lambda = rule();
                    predicates.Add(new ExpressionTranslator(child, lambda.Parameters[0], dialect, values, kinds, joins, capture, innerAliases).Predicate(lambda.Body));
                }

                if (child.RowPredicate is { } fixedPredicate)
                    predicates.Add(fixedPredicate);
                var orderings = node.Orderings.Select(o => new ExpressionTranslator(child, o.Key.Parameters[0], dialect, values, kinds, joins, capture, innerAliases).Column(o.Key.Body) + (o.Descending ? " DESC" : "")).ToList();
                var columns = child.Columns.Select(c => c.Select("t")).ToList();
                Joins(node.Children, "t", joins, columns, (target, alias) => ExpressionTranslator.JoinFilter(target, alias, dialect, values, kinds, joins, capture));
                var collection = node.Collection!;
                SqlKeyMatch.Check(keyOwner, collection);
                var parentKey = SqlKeyMatch.ParentKeys(keyOwner, parentAlias, collection.IsComposite); // a composite parent's key columns as k0, k1, …
                var foreignKey = collection.ForeignKeyColumn;
                var foreignKeys = collection.ForeignKeyColumns;
                // Through a join table: the link joined to the children, filtered on its parent column, which comes back as the last column (the loader groups on it).
                var linkJoin = collection.IsLink ? collection.LinkJoin("t", "t_l") : null;
                var side = collection.IsLink ? "t_l" : "t";
                if (collection.IsLink)
                    columns.AddRange(foreignKeys.Select(fk => $"t_l.\"{fk}\"")); // the parent's key from the join table, last (every column of a composite one)
                string Render(string select, bool keysOnly = false)
                {
                    // keysOnly: as the keys subquery of a companion below, the ordering goes (it means nothing there, and SQL Server refuses it).
                    var sql = new StringBuilder();
                    if (node.Take is { } take)
                    {
                        // A Take per parent: the children numbered by a window partitioned by the foreign key, in a derived table, the first n of each kept (the generator's form).
                        sql.Append("SELECT ").Append(select).Append(" FROM (SELECT t.*, ROW_NUMBER() OVER (PARTITION BY ").Append(SqlKeyMatch.List(foreignKeys, "t")).Append(" ORDER BY ").Append(string.Join(", ", orderings)).Append(") AS rn");
                        sql.Append(" FROM \"").Append(child.Table).Append("\" t WHERE (").Append(SqlKeyMatch.ParentRows(dialect, "t", foreignKeys, renderParent(parentKey))).Append(')');
                        foreach (var predicate in predicates)
                            sql.Append(" AND (").Append(predicate).Append(')');
                        sql.Append(") t");
                        foreach (var join in joins)
                            sql.Append(' ').Append(SqlNavigation.Inner(join, innerAliases));
                        sql.Append(" WHERE (t.rn <= ").Append(take).Append(')');
                        if (!keysOnly)
                            sql.Append(" ORDER BY ").Append(SqlKeyMatch.List(foreignKeys, "t")).Append(", t.rn");
                        return sql.ToString();
                    }

                    sql.Append("SELECT ").Append(select).Append(" FROM \"").Append(child.Table).Append("\" t");
                    if (linkJoin is not null)
                        sql.Append(' ').Append(linkJoin);
                    foreach (var join in joins)
                        sql.Append(' ').Append(SqlNavigation.Inner(join, innerAliases));
                    sql.Append(" WHERE (").Append(SqlKeyMatch.ParentRows(dialect, side, foreignKeys, renderParent(parentKey))).Append(')');
                    foreach (var predicate in predicates)
                        sql.Append(" AND (").Append(predicate).Append(')');
                    if (orderings.Count > 0 && !keysOnly)
                        sql.Append(" ORDER BY ").Append(string.Join(", ", orderings));
                    return sql.ToString();
                }

                statements.Add(Render(string.Join(", ", columns)));
                var keyOrdinal = columns.Count - foreignKeys.Length;
                var keyReaders = Array.ConvertAll(keyOwner.KeyColumns, k => Array.Find(keyOwner.Columns, c => c.Column == k)!);
                companions.Add(new Companion(node, Reader(child.ReadBoxed, node.Children, child.Columns.Length), collection.IsLink ? (r, d) => LinkKey(r, d, keyReaders, keyOrdinal) : null));
                Build(node.Children, dialect, s => Render(s, keysOnly: true), "t", child, companions, statements, values, kinds, capture);
            }
            else
            {
                var alias = parentAlias == "t" ? "j_" + node.Property : parentAlias + "_" + node.Property;
                Build(node.Children, dialect, renderParent, alias, node.Target, companions, statements, values, kinds, capture);
            }
        }
    }

    /// <summary>The parent's key a join-table row carries, read from <paramref name="ordinal" /> on: the column's value, or a <see cref="SqlCompositeKey" /> of a composite key's columns.</summary>
    internal static object? LinkKey(DbDataReader reader, SqlDialect dialect, SqlColumn[] keyColumns, int ordinal)
    {
        if (keyColumns.Length == 1)
            return keyColumns[0].Read(reader, ordinal, dialect);
        var values = new object?[keyColumns.Length];
        for (var i = 0; i < values.Length; i++)
            values[i] = keyColumns[i].Read(reader, ordinal + i, dialect);
        return new SqlCompositeKey(values);
    }

    private static async Task LoadAsync(DbDataReader reader, SqlDialect dialect, List<object> parents, SqlEntityMap map, List<RuntimeInclude> nodes, List<Companion> companions, Cursor cursor, CancellationToken ct)
    {
        foreach (var node in nodes)
        {
            if (node.IsCollection)
            {
                // The next result set is this collection's, whether or not any parent is there: the statements are fixed.
                var companion = companions[cursor.Index++];
                await reader.NextResultAsync(ct).ConfigureAwait(false);
                var children = new List<object>();
                var collection = node.Collection!;
                var groups = new Dictionary<object, List<object>>();
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var child = companion.Read(reader, dialect);
                    children.Add(child);
                    var key = (companion.LinkKey is null ? collection.ChildKey(child) : companion.LinkKey(reader, dialect))!;
                    if (!groups.TryGetValue(key, out var group))
                        groups[key] = group = [];
                    group.Add(child);
                }

                foreach (var parent in parents)
                    collection.Assign(parent, map.WholeKeyOf(parent) is { } key && groups.TryGetValue(key, out var mine) ? mine : []);

                // The children's own to-one includes came with their rows; the collections below them follow, for the children as parents.
                await LoadAsync(reader, dialect, children, node.Target, node.Children, companions, cursor, ct).ConfigureAwait(false);
            }
            else if (node.LoadsAnything)
            {
                var included = new List<object>(parents.Count);
                foreach (var parent in parents)
                {
                    if (node.Navigation!.Get(parent) is { } one)
                        included.Add(one);
                }

                await LoadAsync(reader, dialect, included, node.Target, node.Children, companions, cursor, ct).ConfigureAwait(false);
            }
        }
    }
}
