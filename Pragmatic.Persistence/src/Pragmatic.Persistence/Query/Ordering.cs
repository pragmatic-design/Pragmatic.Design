using System.Linq.Expressions;

namespace Pragmatic.Persistence.Query;

/// <summary>
///     The sort of a read chosen at runtime, the same on every engine: its keys in order, each a member of <typeparamref name="T" /> and a
///     direction. <see cref="Repository.IReadRepository{TEntity}.PageAsync" /> reads it.
/// </summary>
/// <remarks>
///     Each key carries its member's name and its typed expression, both written by the generator from one declaration, so they cannot
///     name different members. EF Core orders by the expression (<see cref="ApplyTo" />). The Sql and documents engines order by the
///     name, which their generated repository maps to the key it compiled for that member; a name it has no key for is refused there.
/// </remarks>
/// <typeparam name="T">The entity whose rows are ordered.</typeparam>
public sealed class Ordering<T>
    where T : class
{
    private readonly (OrderingKey Key, Func<IQueryable<T>, bool, IQueryable<T>> Apply)[] _keys;

    private Ordering((OrderingKey, Func<IQueryable<T>, bool, IQueryable<T>>)[] keys) => _keys = keys;

    /// <summary>No key: the rows in the order the store returns them, or the engine's fallback (the entity's key).</summary>
    public static Ordering<T> Unordered { get; } = new([]);

    /// <summary>The keys, in order.</summary>
    public IReadOnlyList<OrderingKey> Keys => [.. _keys.Select(k => k.Key)];

    /// <summary>This ordering, then <paramref name="key" /> (the member <paramref name="member" />), ascending or descending.</summary>
    /// <param name="member">The member's path on the entity, as <paramref name="key" /> reads it.</param>
    /// <param name="key">The member, as an expression.</param>
    /// <param name="descending">True for a descending key.</param>
    public Ordering<T> Then<TKey>(string member, Expression<Func<T, TKey>> key, bool descending)
    {
        Ensure.Ensure.ThrowIfNullOrWhiteSpace(member);
        Ensure.Ensure.ThrowIfNull(key);
        return new([.. _keys, (new OrderingKey(member, descending), (query, first) => first
            ? descending ? query.OrderByDescending(key) : query.OrderBy(key)
            : descending ? ((IOrderedQueryable<T>)query).ThenByDescending(key) : ((IOrderedQueryable<T>)query).ThenBy(key))]);
    }

    /// <summary>The keys applied to <paramref name="query" />: the first an <c>OrderBy</c>, the next ones <c>ThenBy</c>.</summary>
    public IQueryable<T> ApplyTo(IQueryable<T> query)
    {
        Ensure.Ensure.ThrowIfNull(query);
        for (var i = 0; i < _keys.Length; i++)
            query = _keys[i].Apply(query, i == 0);
        return query;
    }
}
