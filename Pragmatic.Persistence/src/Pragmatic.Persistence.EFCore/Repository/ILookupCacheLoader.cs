namespace Pragmatic.Persistence.EFCore.Repository;

/// <summary>
///     Marker interface for lookup cache loaders.
///     Each <c>[Lookup]</c> entity gets a generated loader that preloads the cache from the DB.
/// </summary>
public interface ILookupCacheLoader
{
    /// <summary>Loads data from the database into the in-memory cache, for every tenant that needs it.</summary>
    Task LoadAsync(IServiceProvider serviceProvider, CancellationToken ct);

    /// <summary>
    ///     Loads the cache for one tenant that appeared after startup.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Called by <see cref="LookupCacheTenantObserver" /> off the tenant lifecycle signal.
    ///         <see cref="LoadAsync" /> enumerates the tenants known at startup and stops there, so
    ///         without this a tenant created while the host runs would have no cache until a restart.
    ///     </para>
    ///     <para>
    ///         A lookup that is not tenant-scoped has one cache for the whole process and does nothing
    ///         here. The generated loader decides which of the two it is at compile time — it is a
    ///         property of the entity, not of the call.
    ///     </para>
    /// </remarks>
    Task LoadForTenantAsync(IServiceProvider serviceProvider, string tenantId, CancellationToken ct);
}
