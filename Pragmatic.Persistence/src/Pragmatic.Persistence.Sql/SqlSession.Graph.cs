using System.Collections;
using System.Linq;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Writes of a graph in one command: the root and, for every collection its tracking says changed (<see cref="IChangeTracking.CollectionsModified" />;
///     every collection of a new row, which has nothing in the database to reconcile),
///     the children removed (a DELETE of the rows under the parent whose key is not among the children's: no read, no tracking
///     collection), added (an insert) or modified (their modified columns), recursively. A delete cascades from the model: the
///     children of every collection, the grandchildren through a subquery, then the root. One transaction, one round trip.
/// </summary>
public sealed partial class SqlSession
{
    /// <summary>Several writes in one command (see <see cref="SqlWriteBatch" />).</summary>
    public SqlWriteBatch WriteBatch() => new(this);

    /// <summary>
    ///     Saves the root and the changes of its graph: the root inserted when new or updated in its modified columns, then for each
    ///     collection in its <see cref="IChangeTracking.CollectionsModified" /> (each of a new row's) the children not in it any more deleted, the new ones
    ///     inserted, the modified ones updated, and their own collections the same way. Every row of the graph tracks its changes
    ///     and carries its foreign key (the parent sets it when it adds the child). Returns how many statements ran.
    /// </summary>
    public async Task<int> SaveGraphAsync<T>(T root, CancellationToken ct = default) where T : class, IChangeTracking
    {
        var batch = WriteBatch();
        var visited = new List<IChangeTracking>();
        AddGraph(batch, SqlEntityMap<T>.Current, root, visited);
        await batch.ExecuteAsync(ct).ConfigureAwait(false);
        foreach (var row in visited)
        {
            row.IsNew = false;
            row.ResetModifiedProperties();
        }

        return batch.Count;
    }

    /// <summary>Deletes the root and everything under it, from the model: the grandchildren, the children of every collection, then the root (at its version, when it has one). One command.</summary>
    public async Task DeleteGraphAsync<T>(T root, CancellationToken ct = default) where T : class
    {
        var map = SqlEntityMap<T>.Current.Concrete(root); // a TPC root's row: its own type's table and collections
        var batch = WriteBatch();
        var (kinds, key) = KeyValues(map, root);
        var writer = map.WriterFor(root);
        if (writer.SoftDeletes)
        {
            // [SoftDelete]: the root marked; with Cascade, the [SoftDelete] rows of its collections too (the others stay as they are). One
            // instant for all of them: the rows a delete marked together are the ones with the root's DeletedAt, which a restore can find.
            var at = DateTimeOffset.UtcNow;
            if (map.SoftDeleteCascade)
                SoftCascade(batch, map, null, kinds, key, at);
            batch.AddWrite(writer, root, writer.SoftDeleteItemAt(root, Dialect, at), before: true, after: false);
        }
        else
        {
            Cascade(batch, map, null, kinds, key);
            batch.AddWrite(writer, root, writer.DeleteItem(root, Dialect), before: true, after: false);
        }

        await batch.ExecuteAsync(ct).ConfigureAwait(false);
    }

    private static void AddGraph(SqlWriteBatch batch, SqlEntityMap map, object row, List<IChangeTracking> visited)
    {
        map = map.Concrete(row); // a TPC root's row: its own type's writer, table and collections
        if (row is not IChangeTracking tracking)
            throw new NotSupportedException($"{map.Type.Name} does not track its changes: every row of a saved graph implements IChangeTracking.");
        visited.Add(tracking);
        var writer = map.WriterFor(row);
        if (tracking.IsNew)
            batch.AddWrite(writer, row, writer.InsertItem(row, batch.Session.Dialect), before: false, after: true);
        else if (writer.SaveItem(row, batch.Session.Dialect) is { } save)
            batch.AddWrite(writer, row, save, before: writer.SaveMovesRollUps(row), after: writer.SaveMovesRollUps(row));

        // A new row's collections are all its children to insert (there are no orphans to look for); a stored row's, those it says changed.
        foreach (var name in tracking.IsNew ? map.Collections.Select(c => c.Property) : tracking.CollectionsModified)
        {
            var collection = map.Collection(name) ?? throw new InvalidOperationException($"'{name}' is not a collection of {map.Type.Name}: CollectionsModified names its one-to-many navigations.");
            var childMap = SqlEntityMap.For(collection.Child) ?? throw new InvalidOperationException($"No map is registered for {collection.Child.Name}: it is not an [Entity] of a translated assembly.");
            if (collection.IsLink)
            {
                AddLinks(batch, map, tracking, collection, childMap, visited);
                continue;
            }

            var childWriter = childMap.WriterBoxed ?? throw new NotSupportedException(childMap.Dispatch
                ? $"{collection.Child.Name} is the root of a table-per-concrete-type hierarchy: the children removed from the collection would be looked for in every derived type's table; save them through their own types."
                : $"No writer is registered for {collection.Child.Name}.");
            var children = new List<object>();
            var kept = new List<object?>();
            if (collection.Get(row) is IEnumerable current)
            {
                foreach (var child in current)
                {
                    children.Add(child);
                    if (child is IChangeTracking { IsNew: false })
                        kept.Add(childMap.KeyOf(child));
                }
            }

            // The rows under the parent that are not among its children any more: gone. A new parent has none. A composite parent: matched on every key column.
            if (!tracking.IsNew)
            {
                SqlKeyMatch.Check(map, collection);
                var (kinds, key) = KeyValues(map, row);
                foreach (var rollUp in childWriter.OrphansRollUps(collection.ForeignKeyColumns, kinds, key, kept))
                    batch.Add(rollUp);
                batch.Add(childWriter.OrphansItem(collection.ForeignKeyColumns, kinds, key, kept));
            }
            foreach (var child in children)
                AddGraph(batch, childMap, child, visited);
        }
    }

    /// <summary>
    ///     A many-to-many side the tracking says changed: the parent's links to rows no longer in the list deleted, a link to each row in it
    ///     inserted where missing (the join table's key keeps it unique), the rows themselves saved first when they track their changes
    ///     (a new one must be there before its link); an untracked row is left as it is.
    /// </summary>
    private static void AddLinks(SqlWriteBatch batch, SqlEntityMap map, IChangeTracking parent, SqlCollection collection, SqlEntityMap childMap, List<IChangeTracking> visited)
    {
        SqlKeyMatch.Check(map, collection); // a composite parent: one join-table column per key column
        var (parentKinds, parentKey) = KeyValues(map, parent);
        var children = new List<object>();
        if (collection.Get(parent) is IEnumerable current)
        {
            foreach (var child in current)
                children.Add(child);
        }

        var childKinds = KeyValues(childMap, null).Kinds;
        if (!parent.IsNew)
            batch.Add(SqlLinkItems.Orphans(collection, parentKinds, parentKey, childKinds, children.ConvertAll(c => KeyValues(childMap, c).Values)));
        foreach (var child in children)
        {
            if (child is IChangeTracking)
                AddGraph(batch, childMap, child, visited);
            batch.Add(SqlLinkItems.Ensure(collection, parentKinds, parentKey, childKinds, KeyValues(childMap, child).Values));
        }
    }

    /// <summary>The deletes under a parent, deepest first: a child's rows are those whose foreign key is the root's key, or is among the keys of the parent rows (a subquery per level); a many-to-many side loses its links, its rows stay.</summary>
    private static void Cascade(SqlWriteBatch batch, SqlEntityMap map, Func<SqlDialect, string>? parentRows, SqlParamKind[] keyKinds, object?[] key)
    {
        foreach (var collection in map.Collections)
        {
            SqlKeyMatch.Check(map, collection); // a composite parent: one foreign key (or join-table column) per key column
            if (collection.IsLink)
            {
                batch.Add(SqlLinkItems.DeleteWhere(collection, RowsUnder(map, parentRows, collection.LinkTable!, collection.ForeignKeyColumns), keyKinds, key));
                continue;
            }

            var childMap = SqlEntityMap.For(collection.Child) ?? throw new InvalidOperationException($"No map is registered for {collection.Child.Name}: it is not an [Entity] of a translated assembly.");
            var childWriter = childMap.WriterBoxed ?? throw new NotSupportedException(childMap.Dispatch
                ? $"{collection.Child.Name} is the root of a table-per-concrete-type hierarchy: a cascade through the model would delete from every derived type's table; delete those rows first."
                : $"No writer is registered for {collection.Child.Name}.");
            var childRows = RowsUnder(map, parentRows, childMap.Table, collection.ForeignKeyColumns);
            Cascade(batch, childMap, childRows, keyKinds, key);
            foreach (var rollUp in childWriter.DeleteWhereRollUps(childRows, keyKinds, key))
                batch.Add(rollUp);
            batch.Add(childWriter.DeleteWhereItem(childRows, keyKinds, key));
        }
    }

    /// <summary>The soft delete under a [SoftDelete] parent with Cascade: each collection's [SoftDelete] rows marked, deepest first where they cascade too.</summary>
    private static void SoftCascade(SqlWriteBatch batch, SqlEntityMap map, Func<SqlDialect, string>? parentRows, SqlParamKind[] keyKinds, object?[] key, DateTimeOffset at)
    {
        foreach (var collection in map.Collections)
        {
            if (collection.IsLink || SqlEntityMap.For(collection.Child) is not { } childMap || childMap.WriterBoxed is not { SoftDeletes: true } childWriter)
                continue; // links and rows that do not soft delete stay
            SqlKeyMatch.Check(map, collection);
            var childRows = RowsUnder(map, parentRows, childMap.Table, collection.ForeignKeyColumns);
            if (childMap.SoftDeleteCascade)
                SoftCascade(batch, childMap, childRows, keyKinds, key, at);
            foreach (var rollUp in childWriter.DeleteWhereRollUps(childRows, keyKinds, key))
                batch.Add(rollUp); // the parent stays: its roll-ups lose the rows marked
            batch.Add(childWriter.SoftDeleteWhereItem(childRows, keyKinds, key, at));
        }
    }

    /// <summary>
    ///     The rows of <paramref name="table" /> under the parent rows: their foreign keys equal to the root's key (a parameter per column) at the first
    ///     level; below it among the keys of the parent rows the condition selects (IN for a single column, EXISTS matched column by column).
    /// </summary>
    private static Func<SqlDialect, string> RowsUnder(SqlEntityMap map, Func<SqlDialect, string>? parentRows, string table, string[] foreignKeys)
    {
        string Q(SqlDialect d, string name) => SqlDialects.Quote(d, name);
        if (parentRows is null)
            return d => string.Join(" AND ", foreignKeys.Select((fk, k) => $"{Q(d, fk)} = @p{k}"));
        if (foreignKeys.Length == 1)
            return d => $"{Q(d, foreignKeys[0])} IN (SELECT {Q(d, map.KeyColumn)} FROM {Q(d, map.Table)} WHERE {parentRows(d)})";
        return d => $"EXISTS (SELECT 1 FROM {Q(d, map.Table)} WHERE {string.Join(" AND ", foreignKeys.Select((fk, k) => $"{Q(d, map.Table)}.{Q(d, map.KeyColumns[k])} = {Q(d, table)}.{Q(d, fk)}"))} AND ({parentRows(d)}))";
    }

    /// <summary>A row's key as parameters: its column's kind and value, or every key column's for a composite key (the kinds alone for a null row).</summary>
    internal static (SqlParamKind[] Kinds, object?[] Values) KeyValues(SqlEntityMap map, object? row)
    {
        var columns = KeyColumnsOf(map);
        var kinds = Array.ConvertAll(columns, c => c.Kind);
        if (row is null)
            return (kinds, []);
        if (!map.HasCompositeKey)
            return (kinds, [map.KeyOf(row)]);
        return (kinds, [.. ((SqlCompositeKey)map.WholeKeyOf(row)!).Values]);
    }
}
