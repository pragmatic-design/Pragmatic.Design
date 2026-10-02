using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pragmatic.Mapping.Mutation;

namespace Pragmatic.Mapping.EFCore.Mutation;

/// <summary>
///     The mutation helpers, taking the navigation <em>entry</em> rather than the navigation itself.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <see cref="MutationHelpers" /> receives an <c>ICollection</c>, and in EF a collection
///         that was never included and an empty one are the same object. A keyed merge decides what
///         to keep by looking at what is there, so against an unloaded collection every incoming
///         element looks new: a write that sends the same rows back inserts them again beside the
///         existing ones, with no exception and a successful <c>SaveChanges</c>. A reference navigation
///         fails the same way for the opposite reason — it looks absent, so a second child is built
///         beside the one already in the database.
///     </para>
///     <para>
///         The parameter type is the safeguard, not a check bolted on top. A
///         <see cref="CollectionEntry{TEntity, TRelatedEntity}" /> cannot be obtained without a
///         <see cref="DbContext" />, and it carries <see cref="NavigationEntry.IsLoaded" /> with it,
///         so the question is answered where the write happens instead of being guessed. Nothing here
///         warns about a correct call site: the call that could be wrong does not type-check.
///     </para>
///     <para>
///         Changing the strategy is not an alternative. <see cref="CollectionStrategy.AddOnly" />
///         removes nothing, but it still reads the collection to decide what is <em>new</em>, so
///         against an unloaded one it duplicates exactly as <see cref="CollectionStrategy.Sync" />
///         does. Measured. The only remedy is to load the navigation.
///     </para>
///     <para>
///         A detached entity is written without complaint: it was built in memory, so it has no
///         unloaded navigation to be wrong about.
///     </para>
/// </remarks>
public static class EfMutationHelpers
{
    /// <summary>
    ///     Maps a 1:N collection navigation from DTO to entity, through its tracked entry.
    /// </summary>
    /// <typeparam name="TEntity">The entity owning the collection.</typeparam>
    /// <typeparam name="TRelatedDto">The related DTO type.</typeparam>
    /// <typeparam name="TRelated">The related entity type.</typeparam>
    /// <typeparam name="TKey">The key used to match a DTO item to an entity item.</typeparam>
    /// <param name="dtoItems">The incoming items (may be null).</param>
    /// <param name="entityCollection">
    ///     The tracked collection entry, as <c>context.Entry(order).Collection(o =&gt; o.Lines)</c>.
    /// </param>
    /// <param name="dtoKeySelector">Extracts the matching key from a DTO item.</param>
    /// <param name="entityKeySelector">Extracts the matching key from an entity item.</param>
    /// <param name="factory">Creates a new entity from a DTO item.</param>
    /// <param name="updater">Updates an existing entity from a DTO item.</param>
    /// <param name="strategy">The collection synchronization strategy (default: Sync).</param>
    /// <exception cref="InvalidOperationException">
    ///     When the collection was never loaded on a tracked entity, naming it.
    /// </exception>
    public static void MapOneToMany<TEntity, TRelatedDto, TRelated, TKey>(
        IEnumerable<TRelatedDto>? dtoItems,
        CollectionEntry<TEntity, TRelated> entityCollection,
        Func<TRelatedDto, TKey> dtoKeySelector,
        Func<TRelated, TKey> entityKeySelector,
        Func<TRelatedDto, TRelated> factory,
        Action<TRelatedDto, TRelated>? updater = null,
        CollectionStrategy strategy = CollectionStrategy.Sync)
        where TEntity : class
        where TRelatedDto : class
        where TRelated : class
        where TKey : notnull
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(entityCollection);

        // Checked before the load question: Ignore means the caller is not writing this collection,
        // so demanding that it be loaded would be theatre.
        if (strategy == CollectionStrategy.Ignore)
            return;

        EnsureLoaded(entityCollection);

        if (entityCollection.CurrentValue is not ICollection<TRelated> items)
        {
            var what = entityCollection.CurrentValue is null ? "null" : "not an ICollection";
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name}.{entityCollection.Metadata.Name} is {what}, so it cannot "
                + "be merged into. Initialise the navigation to a mutable collection.");
        }

        MutationHelpers.MapOneToMany(
            dtoItems, items, dtoKeySelector, entityKeySelector, factory, updater, strategy);
    }

    /// <summary>
    ///     Links a navigation to rows named only by their <b>keys</b>, without loading them.
    /// </summary>
    /// <typeparam name="TEntity">The entity owning the navigation.</typeparam>
    /// <typeparam name="TRelated">The related entity type.</typeparam>
    /// <typeparam name="TKey">The related entity's key type.</typeparam>
    /// <param name="ids">The keys the caller sent, or null when it sent nothing.</param>
    /// <param name="collection">The navigation entry, which answers the load question.</param>
    /// <param name="keyName">The key property's name on <typeparamref name="TRelated" />.</param>
    /// <param name="stubFactory">Builds an empty instance — the generated <c>Create()</c>.</param>
    /// <param name="strategy">How the set of links is written.</param>
    /// <remarks>
    ///     <para>
    ///         The case a list of ids exists for: choosing which rows a parent points at, when those
    ///         rows are not being edited. Loading them to link them is a query per write that buys
    ///         nothing — EF needs the key and nothing else to write the join row.
    ///     </para>
    ///     <para>
    ///         A row that is not tracked is attached as a <b>stub</b>: an instance carrying its key and
    ///         marked <c>Unchanged</c>, so EF writes the link and leaves the row alone. The key is set
    ///         through the entry rather than the property, because an entity that keeps its identity
    ///         private has no public setter for it — which is the normal shape here.
    ///     </para>
    ///     <para>
    ///         ⚠️ Removing a link is not the same as deleting a row, and which one happens is the
    ///         relationship's business: a many-to-many drops the join row, a one-to-many orphans or
    ///         cascades according to its configuration. Same division of labour as
    ///         <see cref="CollectionStrategy.Sync" /> on a collection of children.
    ///     </para>
    ///     <para>
    ///         ⚠️ A null <paramref name="ids" /> is "not telling you about this", not "unlink
    ///         everything" — the same reading a null child gets. An empty list <em>is</em> "none".
    ///     </para>
    /// </remarks>
    public static void MapIdsToMany<TEntity, TRelated, TKey>(
        IEnumerable<TKey>? ids,
        CollectionEntry<TEntity, TRelated> collection,
        string keyName,
        Func<TRelated> stubFactory,
        CollectionStrategy strategy = CollectionStrategy.Sync)
        where TEntity : class
        where TRelated : class
        where TKey : notnull
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(collection);

        MapIdsToMany(
            ids?.Select(static id => (object)id), collection, keyName, stubFactory, strategy);
    }

    /// <summary>
    ///     The same, from a navigation named at runtime and keys already boxed.
    /// </summary>
    /// <remarks>
    ///     What a generated repository has to work with: it implements
    ///     <c>INavigationLinker.LinkAsync</c>, where the navigation arrives as a name and the keys as
    ///     objects. Boxing costs an allocation per key and buys the one thing that matters — the
    ///     related type stays a type parameter, so nothing here creates an instance by reflection.
    /// </remarks>
    public static void MapIdsToMany<TRelated>(
        IEnumerable<object>? ids,
        NavigationEntry collection,
        string keyName,
        Func<TRelated> stubFactory,
        CollectionStrategy strategy = CollectionStrategy.Sync)
        where TRelated : class
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(collection);
        Pragmatic.Ensure.Ensure.ThrowIfNullOrWhiteSpace(keyName);
        Pragmatic.Ensure.Ensure.ThrowIfNull(stubFactory);

        if (strategy == CollectionStrategy.Ignore || ids is null)
            return;

        EnsureLoaded(collection);

        if (collection.CurrentValue is not ICollection<TRelated> links)
        {
            var what = collection.CurrentValue is null ? "null" : "not an ICollection";
            throw new InvalidOperationException(
                $"{collection.EntityEntry.Metadata.ClrType.Name}.{collection.Metadata.Name} is {what}, "
                + "so its links cannot be written. Initialise the navigation to a mutable collection.");
        }

        var context = collection.EntityEntry.Context;
        var wanted = new HashSet<object>(ids);

        if (strategy == CollectionStrategy.Replace)
        {
            links.Clear();
        }
        else
        {
            foreach (var linked in links.ToList())
            {
                var key = KeyOf(context, linked, keyName);

                // AddOnly removes nothing; Sync keeps only what the caller named.
                if (strategy == CollectionStrategy.Sync && !wanted.Contains(key))
                    links.Remove(linked);
                else
                    wanted.Remove(key);
            }
        }

        foreach (var id in wanted)
            links.Add(Stub(context, id, keyName, stubFactory));
    }

    /// <summary>The key EF holds for an entity, read through the entry so a private setter is fine.</summary>
    private static object KeyOf<TRelated>(DbContext context, TRelated entity, string keyName)
        where TRelated : class
        => context.Entry(entity).Property(keyName).CurrentValue!;

    /// <summary>
    ///     An instance carrying only its key, attached as <c>Unchanged</c> so EF writes the link and
    ///     not the row.
    /// </summary>
    /// <remarks>
    ///     An already-tracked instance is reused: attaching a second object for the same key is what
    ///     produces "the instance of entity type cannot be tracked because another instance with the
    ///     same key value is already being tracked".
    /// </remarks>
    private static TRelated Stub<TRelated>(
        DbContext context, object id, string keyName, Func<TRelated> stubFactory)
        where TRelated : class
    {
        // ChangeTracker.Entries, not Set<TRelated>().Local: the two answer the same question — what
        // is already tracked — but Set<T> declares a trimming requirement on T that would have to be
        // carried by every caller up to the generated invoker. Reading the tracker declares none.
        foreach (var tracked in context.ChangeTracker.Entries<TRelated>())
        {
            if (Equals(tracked.Property(keyName).CurrentValue, id))
                return tracked.Entity;
        }

        // The factory, not `new`: an entity's construction is its own — the same door ToEntity and
        // the invoker go through — and a required property would make `new TRelated()` impossible.
        var stub = stubFactory();
        var entry = context.Entry(stub);
        entry.Property(keyName).CurrentValue = id;
        entry.State = EntityState.Unchanged;
        return stub;
    }

    /// <summary>
    ///     Maps a 1:1 reference navigation from DTO to entity, through its tracked entry.
    /// </summary>
    /// <typeparam name="TEntity">The entity owning the reference.</typeparam>
    /// <typeparam name="TRelatedDto">The related DTO type.</typeparam>
    /// <typeparam name="TRelated">The related entity type.</typeparam>
    /// <param name="dtoValue">The incoming value (null detaches the navigation).</param>
    /// <param name="reference">
    ///     The tracked reference entry, as <c>context.Entry(order).Reference(o =&gt; o.Address)</c>.
    /// </param>
    /// <param name="setter">Sets the related entity on the parent.</param>
    /// <param name="factory">Creates a new related entity from the DTO.</param>
    /// <param name="updater">Updates the existing related entity from the DTO.</param>
    /// <remarks>
    ///     The entry answers the load question; the write still goes through <paramref name="setter" />
    ///     rather than <c>reference.CurrentValue</c>. An entity that keeps its state private is written
    ///     through the setter its own generator produced, and routing the assignment through EF instead
    ///     would quietly change which code runs on every nested update.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     When the reference was never loaded on a tracked entity, naming it.
    /// </exception>
    public static void MapOneToOne<TEntity, TRelatedDto, TRelated>(
        TRelatedDto? dtoValue,
        ReferenceEntry<TEntity, TRelated> reference,
        Action<TRelated?> setter,
        Func<TRelatedDto, TRelated> factory,
        Action<TRelatedDto, TRelated>? updater = null)
        where TEntity : class
        where TRelatedDto : class
        where TRelated : class
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(reference);

        EnsureLoaded(reference);

        if (dtoValue is null && DetachWouldDestroyARecoverableRow(reference, out var recoverable))
        {
            MarkRecoverable(recoverable!);
            return;
        }

        MutationHelpers.MapOneToOne(
            dtoValue,
            () => reference.CurrentValue,
            setter,
            factory,
            updater);
    }

    /// <summary>
    ///     Whether nulling this reference would take a row <c>[SoftDelete]</c> promised to keep.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Under a <b>required</b> relationship, "remove the link and keep the row" is not
    ///         expressible: the dependent's key cannot be null, so an orphan is a row EF must delete.
    ///         ⚠️ And that deletion does not survive the soft-delete mark. Measured: the change
    ///         tracker holds the dependent as <c>Deleted</c> before the save, the interceptor flips it
    ///         to <c>Modified</c> and sets the flag — and the row is gone anyway, because the link is
    ///         still severed and EF orphans it again. The row was what the promise was about.
    ///     </para>
    ///     <para>
    ///         So the detach is expressed the only way the schema allows: the child is marked deleted
    ///         and keeps its key. Every read goes through the <c>!IsDeleted</c> filter, so the story
    ///         has no frame from every reader's point of view, and the row survives — which is the
    ///         guarantee, and the same one a soft-deletable child removed from a <em>collection</em>
    ///         already got.
    ///     </para>
    ///     <para>
    ///         Only where both hold. An optional relationship nulls the key and keeps the row without
    ///         help; a dependent that is not soft-deletable was never promised anything, and the
    ///         relationship decides its fate — the same division of labour a collection has always had.
    ///     </para>
    /// </remarks>
    private static bool DetachWouldDestroyARecoverableRow<TEntity, TRelated>(
        ReferenceEntry<TEntity, TRelated> reference,
        out Pragmatic.Persistence.Entity.ISoftDelete? recoverable)
        where TEntity : class
        where TRelated : class
    {
        recoverable = reference.CurrentValue as Pragmatic.Persistence.Entity.ISoftDelete;

        return recoverable is not null
               && reference.Metadata is Microsoft.EntityFrameworkCore.Metadata.INavigation
               {
                   ForeignKey.IsRequired: true
               };
    }

    /// <summary>Marks a row deleted without re-stamping one a cascade already stamped.</summary>
    /// <remarks>
    ///     The instant and the actor are the interceptor's business: it runs on every path and shares
    ///     one instant across a cascade, which is what restore reads to decide what comes back.
    ///     Setting them here would put a second writer beside it.
    /// </remarks>
    private static void MarkRecoverable(Pragmatic.Persistence.Entity.ISoftDelete recoverable)
        => recoverable.IsDeleted = true;

    /// <summary>Refuses a navigation the context never loaded, naming it.</summary>
    /// <remarks>
    ///     Detached is not refused: an entity built in memory has nothing to have failed to load.
    /// </remarks>
    private static void EnsureLoaded(NavigationEntry navigation)
    {
        if (navigation.IsLoaded || navigation.EntityEntry.State == EntityState.Detached)
            return;

        throw new InvalidOperationException(
            $"{navigation.EntityEntry.Metadata.ClrType.Name}.{navigation.Metadata.Name} was not "
            + "loaded. A merge cannot see what is already there: a collection writes every element "
            + "again, a reference builds a second child beside the one in the database. Include it "
            + "in the query — changing the collection strategy does not help, because AddOnly also "
            + "reads the collection to decide what is new and duplicates just the same.");
    }
}
