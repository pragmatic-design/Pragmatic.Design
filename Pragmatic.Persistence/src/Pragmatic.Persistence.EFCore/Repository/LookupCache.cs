using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Repository;

/// <summary>
///     In-memory lookup cache backed by a dictionary.
///     Preloaded at startup via <see cref="ILookupCacheLoader"/>.
/// </summary>
public sealed class LookupCache<T, TId> : ILookupCache<T, TId>
    where T : class
    where TId : notnull
{
    // Both fields are swapped by reference on Load() so concurrent readers always observe a
    // fully-built, internally-consistent snapshot (never a half-cleared / half-filled dictionary).
    private volatile Dictionary<TId, T> _cache = new();
    private volatile IReadOnlyList<T> _all = Array.Empty<T>();

    /// <summary>Replaces the entire cache with the given items.</summary>
    public void Load(IEnumerable<T> items, Func<T, TId> idSelector)
    {
        var newCache = new Dictionary<TId, T>();
        var list = new List<T>();
        foreach (var item in items)
        {
            newCache[idSelector(item)] = item;
            list.Add(item);
        }

        // Atomic reference swaps: readers see either the old or the new snapshot, never a torn one.
        _all = list.AsReadOnly();
        _cache = newCache;
    }

    public T Get(TId id) =>
        _cache.TryGetValue(id, out var value)
            ? value
            : throw new KeyNotFoundException($"Lookup entity {typeof(T).Name} with ID '{id}' not found in cache.");

    public bool TryGet(TId id, out T? value) => _cache.TryGetValue(id, out value);

    public IReadOnlyList<T> GetAll() => _all;
}
