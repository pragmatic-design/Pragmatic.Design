using System.Collections.Concurrent;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Ambient resolver for lookup entities.
///     Initialized at application startup, used by generated navigation properties
///     on entity partial classes. Thread-safe, singleton.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Caches are held <b>per tenant</b>. They were held per type, process wide, so whichever
///         tenant was preloaded last answered for every one of them: a per-tenant lookup table served
///         one tenant's row to another. A lookup that is not tenant-scoped registers and reads under no
///         tenant, which is the slot everything used before and is unaffected.
///     </para>
///     <para>
///         The tenant comes from the ambient <see cref="TenantScope" />, and that is forced rather than
///         chosen: a generated navigation property lives on an entity instance, which has no service
///         provider to resolve an <see cref="ITenantContext" /> from. It is also why this resolver is
///         static at all.
///     </para>
/// </remarks>
public static class LookupResolver
{
    private static readonly ConcurrentDictionary<(Type Entity, string? Tenant), object> Caches = new();

    // The ambient reader. One instance is enough: TenantScope's state is a static AsyncLocal.
    private static readonly TenantScope Ambient = new();

    private static string? CurrentTenant => Ambient.TenantId;

    /// <summary>Registers a lookup cache for a specific entity type, under the current tenant.</summary>
    public static void Register<T, TId>(ILookupCache<T, TId> cache)
        where T : class
        where TId : notnull
    {
        Caches[(typeof(T), CurrentTenant)] = cache;
    }

    /// <summary>Gets a lookup entity by ID. Used by generated navigation properties.</summary>
    public static T Get<T, TId>(TId id)
        where T : class
        where TId : notnull
    {
        var tenant = CurrentTenant;

        if (!TryResolve<T, TId>(tenant, out var cache))
            throw new InvalidOperationException(
                $"Lookup cache for {typeof(T).Name} is not registered{Describe(tenant)}. " +
                "Ensure the lookup cache is loaded at startup.");

        return cache!.Get(id);
    }

    /// <summary>Tries to get a lookup entity by ID.</summary>
    public static bool TryGet<T, TId>(TId id, out T? value)
        where T : class
        where TId : notnull
    {
        if (!TryResolve<T, TId>(CurrentTenant, out var cache))
        {
            value = default;
            return false;
        }

        return cache!.TryGet(id, out value);
    }

    /// <summary>
    ///     The cache for this tenant, or the shared one when the lookup is not tenant-scoped.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The fallback is what keeps an ordinary lookup readable from inside a request. Most
    ///     lookup tables — countries, statuses, categories — are the same rows for everyone: they are
    ///     preloaded once, under no tenant, and asking for them under a tenant has to find them.
    ///     Without this, partitioning by tenant would have broken every lookup that is not
    ///     tenant-scoped the moment a request resolved a tenant, which is most of them and most
    ///     requests.
    ///     <para>
    ///         The order matters and only this way round: a tenant's own rows win over the shared
    ///         slot, never the reverse.
    ///     </para>
    /// </remarks>
    private static bool TryResolve<T, TId>(string? tenant, out ILookupCache<T, TId>? cache)
        where T : class
        where TId : notnull
    {
        if (tenant is not null && Caches.TryGetValue((typeof(T), tenant), out var own))
        {
            cache = (ILookupCache<T, TId>)own;
            return true;
        }

        if (Caches.TryGetValue((typeof(T), null), out var shared))
        {
            cache = (ILookupCache<T, TId>)shared;
            return true;
        }

        cache = null;
        return false;
    }

    /// <summary>
    ///     Drops every cache loaded for one tenant, leaving the other tenants and the shared caches
    ///     alone.
    /// </summary>
    /// <remarks>
    ///     Called when a tenant is deactivated or deleted. ⚠️ Not <see cref="Reset" />: one tenant
    ///     leaving must not empty what was loaded for the others, which would be a worse outage than
    ///     the staleness this exists to end. A caller inside the dropped tenant falls back to the
    ///     shared cache, exactly as one that was never preloaded does.
    /// </remarks>
    public static void ForgetTenant(string tenantId)
    {
        foreach (var key in Caches.Keys)
        {
            if (string.Equals(key.Tenant, tenantId, StringComparison.Ordinal))
                Caches.TryRemove(key, out _);
        }
    }

    /// <summary>Clears all registrations. For testing only.</summary>
    public static void Reset() => Caches.Clear();

    /// <summary>
    ///     Names the tenant in a failure, because "not registered" and "not registered for this tenant"
    ///     send the reader to different places.
    /// </summary>
    private static string Describe(string? tenant)
        => tenant is null ? "" : $" for tenant '{tenant}'";
}
