using System.Collections;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The graph a unit of work saves, as EF Core's change tracker saves an aggregate: the children a read brought in its one-to-many collections
///     are tracked with their root, and the save compares each collection with the children it held. A child that is new to it is added, its
///     foreign key set to the parent's key (EF Core's relationship fixup); a child gone from it is deleted, a soft one marked by the save's
///     phases; a child still there is a row like any other, saved in the columns it changed.
/// </summary>
/// <remarks>
///     A collection the read did not load holds no children, so nothing is removed from it: only what was read can go. A row staged without a
///     read (<c>Update</c>, <c>Remove</c>) has no graph, and its children are not compared. A many-to-many side and a collection under a composite parent are not part of the graph (the map gives no foreign key to set), and neither is a to-one
///     navigation.
/// </remarks>
public sealed partial class SqlUnitOfWork
{
    /// <summary>A row read with its graph: tracked as <see cref="Attach{T}" /> tracks a row, its children too, then the collections remembered.</summary>
    private SqlEntry AttachRow(object entity)
    {
        var keyed = Keyed(entity);
        if (_byKey.TryGetValue((entity.GetType(), keyed.PersistenceId), out var tracked))
            return tracked;
        var entry = new SqlEntry(entity, SqlEntryState.Unchanged, Session.Dialect);
        Track(entry);
        foreach (var collection in GraphCollections(entity))
        {
            foreach (var child in Children(collection, entity))
                AttachRow(child);
        }

        Remember(entry);
        return entry;
    }

    /// <summary>
    ///     Before the save sees what changed: every tracked entity's collections against the children they held, the children added tracked as
    ///     added (theirs in turn, as the loop reaches them), the ones removed deleted.
    /// </summary>
    private void DetectGraph()
    {
        for (var i = 0; i < _entries.Count; i++) // the children added are appended, and visited after their parent
        {
            var entry = _entries[i];
            // A new row's children are all new; a read row's are compared with what it was read with. A row staged without a read (Update,
            // Remove) has no graph to compare with: its children are left as they are, as before the unit of work tracked graphs.
            if (entry.State == SqlEntryState.Deleted || (entry.State != SqlEntryState.Added && entry.Members is null))
                continue;
            var map = SqlEntityMap.For(entry.Entity.GetType());
            if (map is null)
                continue;
            var key = map.KeyOf(entry.Entity);
            foreach (var collection in GraphCollections(map))
            {
                var now = new List<SqlEntry>();
                foreach (var child in Children(collection, entry.Entity))
                {
                    collection.SetChildKey!(child, key); // a child moved here from another parent follows it, as a new one does
                    if (EntryOf(child) is { } tracked)
                    {
                        now.Add(tracked);
                        continue;
                    }

                    var keyed = Keyed(child);
                    if (_byKey.TryGetValue((child.GetType(), keyed.PersistenceId), out var other))
                        throw new InvalidOperationException($"{map.Type.Name}.{collection.Property} holds a {child.GetType().Name} with key {keyed.PersistenceId}, and another instance with that key is tracked ({other.State}): add that one.");
                    var added = new SqlEntry(child, SqlEntryState.Added, Session.Dialect);
                    Track(added);
                    now.Add(added);
                }

                if (entry.Members?.TryGetValue(collection, out var before) == true)
                {
                    foreach (var gone in before)
                    {
                        if (now.Contains(gone) || !_entries.Contains(gone))
                            continue;
                        if (gone.State == SqlEntryState.Added)
                            Forget(gone); // never written: nothing to delete
                        else
                            gone.State = SqlEntryState.Deleted;
                    }
                }
            }
        }
    }

    /// <summary>What each collection of a tracked entity holds now, as tracked entries: what the next save compares with.</summary>
    private void Remember(SqlEntry entry)
    {
        Dictionary<SqlCollection, List<SqlEntry>>? members = null;
        foreach (var collection in GraphCollections(entry.Entity))
        {
            var held = new List<SqlEntry>();
            foreach (var child in Children(collection, entry.Entity))
            {
                if (EntryOf(child) is { } tracked)
                    held.Add(tracked);
            }

            (members ??= [])[collection] = held;
        }

        entry.Members = members;
    }

    private static IEnumerable<SqlCollection> GraphCollections(object entity)
        => SqlEntityMap.For(entity.GetType()) is { } map ? GraphCollections(map) : [];

    /// <summary>The collections the graph covers: one-to-many sides whose foreign key the map can set.</summary>
    private static IEnumerable<SqlCollection> GraphCollections(SqlEntityMap map)
    {
        foreach (var collection in map.Collections)
        {
            if (!collection.IsLink && collection.SetChildKey is not null)
                yield return collection;
        }
    }

    private static IEnumerable<object> Children(SqlCollection collection, object entity)
    {
        if (collection.Get(entity) is not IEnumerable children)
            yield break;
        foreach (var child in children)
        {
            if (child is IEntity)
                yield return child;
        }
    }
}
