namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Provides cached access to lookup entities — small, mostly-static reference tables
///     (Country, Status, PaymentMethod, etc.).
///     Preloaded at startup, resolved synchronously. No DB query on each access.
/// </summary>
/// <typeparam name="T">The lookup entity type.</typeparam>
/// <typeparam name="TId">The entity ID type.</typeparam>
public interface ILookupCache<T, in TId>
    where T : class
    where TId : notnull
{
    /// <summary>Gets a lookup entity by its ID. Throws if not found.</summary>
    T Get(TId id);

    /// <summary>Tries to get a lookup entity by its ID.</summary>
    bool TryGet(TId id, out T? value);

    /// <summary>Returns all cached lookup entities.</summary>
    IReadOnlyList<T> GetAll();
}
