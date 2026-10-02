using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Lookups;

/// <summary>
///     A minimal <see cref="ILookupCache{T,TId}"/> for <see cref="Currency"/>. In host mode the SG
///     generates this implementation and a hosted service preloads it from the DB at startup. The
///     contract is what matters here: synchronous, allocation-free resolution by ID — no DB round-trip.
/// </summary>
public sealed class InMemoryCurrencyCache : ILookupCache<Currency, Guid>
{
    private readonly IReadOnlyDictionary<Guid, Currency> _byId;
    private readonly IReadOnlyList<Currency> _all;

    public InMemoryCurrencyCache(IEnumerable<Currency> currencies)
    {
        _all = currencies.ToList();
        _byId = _all.ToDictionary(c => c.Id);
    }

    public Currency Get(Guid id)
        => _byId.TryGetValue(id, out var value)
            ? value
            : throw new KeyNotFoundException($"No currency cached for id '{id}'.");

    public bool TryGet(Guid id, out Currency? value)
        => _byId.TryGetValue(id, out value);

    public IReadOnlyList<Currency> GetAll() => _all;
}
