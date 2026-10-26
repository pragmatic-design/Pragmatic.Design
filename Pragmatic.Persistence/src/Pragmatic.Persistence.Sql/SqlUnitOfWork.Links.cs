using System.Collections;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The links of the many-to-many sides a unit of work saves, as EF Core's change tracker saves a skip navigation: a side a read loaded is
///     remembered, and the save compares it with the rows it was linked to, inserting a row of the join table for a row new to it and deleting
///     the one of a row gone from it. A row in a side needs its key and nothing else, so a stub carrying only the key links as a loaded row does
///     (<c>INavigationLinker</c>). A new entity's sides are all new links.
/// </summary>
/// <remarks>
///     A side the read did not load holds no rows, and whether that means "none" is not known: nothing is compared, and a link of keys refuses
///     it (<see cref="IsLoaded" />). The rows in a side are not tracked: a change to one of them is not saved through its side.
/// </remarks>
public sealed partial class SqlUnitOfWork
{
    /// <summary>
    ///     The row, tracked as <see cref="Attach{T}(T)" /> tracks it, read with <paramref name="includes" /> (comma-separated paths, as
    ///     <c>GetByIdIncludingAsync</c> takes them): the collections they name are loaded, their contents what the store holds.
    /// </summary>
    public T Attach<T>(T entity, string includes) where T : class, IEntity
    {
        var row = Attach(entity);
        MarkLoaded(EntryOf(row)!, includes, read: row);
        return row;
    }

    /// <summary>
    ///     As <see cref="LoadGraph{T}(T, T)" />, <paramref name="read" /> read with <paramref name="includes" />: its many-to-many sides join the
    ///     ones <paramref name="entity" /> holds too, and the collections the paths name are loaded.
    /// </summary>
    public void LoadGraph<T>(T entity, T read, string includes) where T : class, IEntity
    {
        LoadGraph(entity, read);
        var entry = EntryOf(entity)!;
        if (SqlEntityMap.For(entity.GetType()) is not { } map)
            return;
        foreach (var name in FirstSegments(includes))
        {
            if (map.Collection(name) is not { IsLink: true } link || entry.Loaded?.Contains(name) == true)
                continue;
            var merged = Rows(link, entity).ToList();
            foreach (var row in Rows(link, read))
            {
                if (!merged.Contains(row))
                    merged.Add(row);
            }

            link.Assign(entity, merged);
        }

        MarkLoaded(entry, includes, read);
    }

    /// <summary>True when the entity's <paramref name="collection" /> holds what the store holds: a read loaded it, or the entity is new.</summary>
    public bool IsLoaded(object entity, string collection)
        => EntryOf(entity) is { } entry && (entry.State == SqlEntryState.Added || entry.Loaded?.Contains(collection) == true);

    /// <summary>The paths' first segments loaded on the entry, a many-to-many side's rows remembered as <paramref name="read" />'s side holds them.</summary>
    private static void MarkLoaded(SqlEntry entry, string includes, object read)
    {
        if (SqlEntityMap.For(entry.Entity.GetType()) is not { } map)
            return;
        foreach (var name in FirstSegments(includes))
        {
            if (map.Collection(name) is not { } collection)
                continue;
            (entry.Loaded ??= []).Add(name);
            if (collection.IsLink)
                RememberLinks(entry, collection, read);
        }
    }

    /// <summary>The rows a many-to-many side of <paramref name="source" /> holds, by key, remembered on the entry: what the next save compares with.</summary>
    private static void RememberLinks(SqlEntry entry, SqlCollection link, object source)
    {
        if (SqlEntityMap.For(link.Child) is not { CompositeKeyOf: null } childMap)
            return; // a composite child key is not compared: the side is left as the save finds it
        var held = new Dictionary<object, object>();
        foreach (var row in Rows(link, source))
        {
            if (childMap.KeyOf(row) is { } key)
                held[key] = row;
        }

        (entry.Links ??= [])[link] = held;
    }

    /// <summary>
    ///     Before the save writes: every tracked entity's remembered sides against the rows they hold now, and a new entity's sides whole. A
    ///     link to remove carries the row it was remembered with (its key is what the join table's row is deleted by).
    /// </summary>
    private List<(bool Add, object Parent, SqlCollection Link, object Row)> DetectLinks()
    {
        var changes = new List<(bool, object, SqlCollection, object)>();
        foreach (var entry in _entries)
        {
            if (entry.State == SqlEntryState.Deleted || SqlEntityMap.For(entry.Entity.GetType()) is not { } map)
                continue;
            foreach (var link in map.Collections)
            {
                if (!link.IsLink || SqlEntityMap.For(link.Child) is not { CompositeKeyOf: null } childMap)
                    continue;
                Dictionary<object, object>? before = null;
                if (entry.State != SqlEntryState.Added && entry.Links?.TryGetValue(link, out before) != true)
                    continue; // not loaded: nothing to compare with
                var now = new Dictionary<object, object>();
                foreach (var row in Rows(link, entry.Entity))
                {
                    if (childMap.KeyOf(row) is { } key)
                        now[key] = row;
                }

                foreach (var (key, row) in now)
                {
                    if (before is null || !before.ContainsKey(key))
                        changes.Add((true, entry.Entity, link, row));
                }

                if (before is null)
                    continue;
                foreach (var (key, row) in before)
                {
                    if (!now.ContainsKey(key))
                        changes.Add((false, entry.Entity, link, row));
                }
            }
        }

        return changes;
    }

    /// <summary>After the save: the sides compare with what they hold now, a new entity's sides loaded from here on.</summary>
    private void RememberSavedLinks(IEnumerable<SqlEntry> added)
    {
        foreach (var entry in added)
        {
            if (SqlEntityMap.For(entry.Entity.GetType()) is not { } map)
                continue;
            foreach (var collection in map.Collections)
            {
                (entry.Loaded ??= []).Add(collection.Property);
                if (collection.IsLink)
                    RememberLinks(entry, collection, entry.Entity);
            }
        }

        foreach (var entry in _entries)
        {
            if (entry.Links is null)
                continue;
            foreach (var link in entry.Links.Keys.ToList())
                RememberLinks(entry, link, entry.Entity);
        }
    }

    private static IEnumerable<string> FirstSegments(string includes)
        => includes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.Split('.')[0]).Distinct();

    private static IEnumerable<object> Rows(SqlCollection collection, object entity)
    {
        if (collection.Get(entity) is not IEnumerable rows)
            yield break;
        foreach (var row in rows)
        {
            if (row is not null)
                yield return row;
        }
    }
}
