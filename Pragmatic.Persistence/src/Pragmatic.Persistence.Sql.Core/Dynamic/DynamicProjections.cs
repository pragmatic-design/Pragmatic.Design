using System.Data.Common;
using System.Text;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>
///     The collection members of a declared projection on the runtime path: one companion statement per
///     collection in the same command (the child DTO's columns and the child's foreign key, selecting the
///     children of the parent statement's rows through a subquery on the parent key), read from the result
///     sets after the rows' own and grouped on the key the parent DTO reads. The same statements the
///     generator emits for <c>Project&lt;TDto&gt;()</c>; nothing is compiled, the registered readers do the work.
/// </summary>
internal static class DynamicProjections
{
    private sealed class Companion(SqlProjectedCollection collection, SqlProjectionMap childDeclared, Func<DbDataReader, SqlDialect, object?> read, Func<DbDataReader, SqlDialect, object?> key)
    {
        public SqlProjectedCollection Collection { get; } = collection;

        public SqlProjectionMap ChildDeclared { get; } = childDeclared;

        public Func<DbDataReader, SqlDialect, object?> Read { get; } = read;

        /// <summary>The parent key a child row carries, read after the DTO's columns.</summary>
        public Func<DbDataReader, SqlDialect, object?> Key { get; } = key;
    }

    private sealed class Cursor
    {
        public int Index;
    }

    /// <summary>The companions of <paramref name="declared" />'s collections (appended to <paramref name="statements" />, depth first) and the loader reading them in that order; null without collections.</summary>
    public static SqlLoad<T>? Loader<T>(SqlProjectionMap declared, SqlEntityMap map, SqlDialect dialect, Func<string, string> renderParent, List<string> statements, List<object?> values, List<SqlParamKind> kinds, SqlCapture capture)
    {
        if (declared.Collections.Length == 0)
            return null;
        var companions = new List<Companion>();
        Build(declared, map, dialect, renderParent, companions, statements, values, kinds, capture);
        return async (reader, dialect, rows, ct) =>
        {
            var parents = new List<object>(rows.Count);
            foreach (var row in rows)
                parents.Add(row!);
            await LoadAsync(reader, dialect, parents, declared, companions, new Cursor(), ct).ConfigureAwait(false);
        };
    }

    private static void Build(SqlProjectionMap declared, SqlEntityMap keyOwner, SqlDialect dialect, Func<string, string> renderParent, List<Companion> companions, List<string> statements, List<object?> values, List<SqlParamKind> kinds, SqlCapture capture)
    {
        foreach (var collection in declared.Collections)
        {
            var child = SqlEntityMap.For(collection.Child) ?? throw new InvalidOperationException($"No map is registered for {collection.Child.Name}.");
            var childDeclared = SqlProjectionMap.For(collection.Child, collection.ChildDto)
                ?? throw new NotSupportedException($"A projection into {collection.ChildDto.Name} needs it declared: mark it [MapFrom<{collection.Child.Name}>] so the generator registers its columns and reader.");
            var joins = new List<string>();
            foreach (var join in childDeclared.Joins)
            {
                var at = joins.Count;
                joins.Add(join);
                joins[at] = ExpressionTranslator.DeclaredJoin(child, join, dialect, values, kinds, joins, capture); // the child DTO's flattened members: their targets' rules
            }

            var innerAliases = new HashSet<string>(StringComparer.Ordinal);
            var predicates = new List<string>();
            foreach (var rule in child.Rules)
            {
                var lambda = rule();
                predicates.Add(new ExpressionTranslator(child, lambda.Parameters[0], dialect, values, kinds, joins, capture, innerAliases).Predicate(lambda.Body));
            }

            if (child.RowPredicate is { } fixedPredicate)
                predicates.Add(fixedPredicate);
            var foreignKeys = collection.ForeignKeyColumns;
            var parentKey = SqlKeyMatch.ParentKeys(keyOwner, "t", foreignKeys.Length > 1); // a composite parent's key columns as k0, k1, …
            if (keyOwner.Collection(collection.Property) is { IsLink: true })
                throw new NotSupportedException($"{keyOwner.Type.Name}.{collection.Property} is a many-to-many side: a DTO collection is loaded through the child's foreign key; Include the collection on the entity instead.");
            if (keyOwner.HasCompositeKey != foreignKeys.Length > 1)
                throw new NotSupportedException($"{keyOwner.Type.Name} has a composite key ({string.Join(", ", keyOwner.KeyColumns)}): {collection.Child.Name} carries no foreign key per key column.");
            string Render(string select)
            {
                var sql = new StringBuilder();
                sql.Append("SELECT ").Append(select).Append(" FROM \"").Append(child.Table).Append("\" t");
                foreach (var join in joins)
                    sql.Append(' ').Append(SqlNavigation.Inner(join, innerAliases));
                sql.Append(" WHERE (").Append(SqlKeyMatch.ParentRows(dialect, "t", foreignKeys, renderParent(parentKey))).Append(')');
                foreach (var predicate in predicates)
                    sql.Append(" AND (").Append(predicate).Append(')');
                return sql.ToString();
            }

            // The child DTO's columns, then the foreign keys: the loader groups on them with the parent's key readers (a composite key's as one SqlCompositeKey).
            statements.Add(Render(string.Join(", ", childDeclared.Columns) + ", " + SqlKeyMatch.List(foreignKeys, "t")));
            var keyColumns = Array.ConvertAll(keyOwner.KeyColumns, k => Array.Find(keyOwner.Columns, c => c.Column == k) ?? throw new InvalidOperationException($"{keyOwner.Type.Name} has no column {k}."));
            var keyOrdinal = childDeclared.Columns.Length;
            companions.Add(new Companion(collection, childDeclared, childDeclared.ReadBoxed, keyColumns.Length == 1
                ? (r, d) => keyColumns[0].Read(r, keyOrdinal, d)
                : (r, d) =>
                {
                    var values = new object?[keyColumns.Length];
                    for (var i = 0; i < values.Length; i++)
                        values[i] = keyColumns[i].Read(r, keyOrdinal + i, d);
                    return new SqlCompositeKey(values);
                }));
            Build(childDeclared, child, dialect, Render, companions, statements, values, kinds, capture);
        }
    }

    private static async Task LoadAsync(DbDataReader reader, SqlDialect dialect, List<object> parents, SqlProjectionMap declared, List<Companion> companions, Cursor cursor, CancellationToken ct)
    {
        foreach (var collection in declared.Collections)
        {
            // The next result set is this collection's, whether or not any parent is there: the statements are fixed.
            var companion = companions[cursor.Index++];
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var children = new List<object>();
            var groups = new Dictionary<object, List<object>>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var child = companion.Read(reader, dialect)!;
                var key = companion.Key(reader, dialect)!;
                if (!groups.TryGetValue(key, out var group))
                    groups[key] = group = [];
                group.Add(child);
                children.Add(child);
            }

            foreach (var parent in parents)
                collection.Assign(parent, collection.ParentKey(parent) is { } key && groups.TryGetValue(key, out var mine) ? mine : []);

            // The collections below the children follow, for the children as parents.
            await LoadAsync(reader, dialect, children, companion.ChildDeclared, companions, cursor, ct).ConfigureAwait(false);
        }
    }
}
