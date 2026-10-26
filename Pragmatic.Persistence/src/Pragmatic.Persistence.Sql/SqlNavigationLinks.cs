using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     <c>INavigationLinker</c> on the Sql engine: the keys a mutation's <c>[LinkIds]</c> names made the rows a collection of a tracked entity
///     points at, the generated repository's <c>LinkAsync</c> handing over the collection and how to read or stub its rows. The unit of work
///     then saves the difference: a many-to-many side's links (<see cref="SqlUnitOfWork.IsLoaded" />), a one-to-many's foreign keys.
/// </summary>
/// <remarks>
///     <para>
///         The strategies are <c>CollectionStrategy</c>'s, as EF Core's <c>MapIdsToMany</c> applies them: <c>Sync</c> keeps only the keys
///         named, <c>AddOnly</c> adds and removes nothing, <c>Replace</c> clears first, <c>Ignore</c> writes nothing.
///     </para>
///     <para>
///         A many-to-many side gets a stub per key (the join table's row needs the key and nothing else). A one-to-many's chosen rows are read
///         by key inside the caller's filters, so a row the caller may not read is not linked: a stub of it would move a row by its key alone.
///     </para>
///     <para>
///         ⚠️ The collection has to hold what the store holds, or a <c>Sync</c> removes nothing and links again what is there: one not loaded is
///         refused, as EF Core's helper refuses it.
///     </para>
/// </remarks>
public static class SqlNavigationLinks
{
    /// <summary>Makes <paramref name="ids" /> the rows <paramref name="side" />, the collection <paramref name="navigation" /> of <paramref name="entity" />, points at.</summary>
    /// <returns>1 when the chosen rows were read, 0 otherwise.</returns>
    public static async Task<int> LinkAsync<TRelated>(SqlUnitOfWork unitOfWork, object entity, string navigation, ICollection<TRelated> side,
        IReadOnlyList<object> ids, string key, string strategy, Func<Guid, TRelated> stub,
        Func<List<Guid>, CancellationToken, Task<List<TRelated>>> readChosen, CancellationToken ct)
        where TRelated : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(side);
        if (strategy == "Ignore")
            return 0;
        if (key != nameof(IEntity.PersistenceId))
            throw new NotSupportedException($"{entity.GetType().Name}.{navigation} is linked by {key}: on the Sql engine a row is linked by its key, PersistenceId.");
        if (!unitOfWork.IsLoaded(entity, navigation))
            throw new InvalidOperationException(
                $"{entity.GetType().Name}.{navigation} was not loaded, so the links it holds are not known: a link of keys decides what to remove by what is there. Read the entity with the navigation.");
        var link = SqlEntityMap.For(entity.GetType())?.Collection(navigation)
                   ?? throw new ArgumentException($"'{navigation}' is not a collection of {entity.GetType().Name}.", nameof(navigation));

        var wanted = new HashSet<Guid>(ids.Select(static id => id is Guid guid ? guid : Guid.Parse(id.ToString()!)));
        if (strategy == "Replace")
        {
            side.Clear();
        }
        else
        {
            foreach (var row in side.ToList())
            {
                if (strategy == "Sync" && !wanted.Contains(row.PersistenceId))
                    side.Remove(row);
                else
                    wanted.Remove(row.PersistenceId);
            }
        }

        if (wanted.Count == 0)
            return 0;
        if (link.IsLink)
        {
            foreach (var id in wanted)
                side.Add(stub(id));
            return 0;
        }

        foreach (var row in await readChosen([.. wanted], ct).ConfigureAwait(false))
            side.Add(unitOfWork.Attach(row));
        return 1;
    }
}
