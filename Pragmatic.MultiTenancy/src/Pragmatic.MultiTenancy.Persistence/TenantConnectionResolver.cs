using System.Collections.Concurrent;

namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Resolves a tenant's dedicated database connection string (or null when the tenant has none and
///     should stay on the shared database). Singleton with a shared TTL + LRU cache keyed by tenant id —
///     the tenant id is passed in (not injected) so a single cache serves every request.
/// </summary>
/// <remarks>
///     Used by <see cref="TenantConnectionInterceptor"/>: the (singleton) interceptor reads the ambient
///     tenant from the DbContext's scoped services and asks this resolver for the connection to open.
/// </remarks>
public sealed class TenantConnectionResolver(ITenantStore tenantStore, TenantDatabaseOptions options)
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Returns the tenant's dedicated connection string, or <c>null</c> when the tenant has none
    ///     (stays on the shared database). Throws <see cref="TenantNotFoundException"/> /
    ///     <see cref="TenantDeactivatedException"/> for an unknown / deactivated tenant.
    /// </summary>
    public async ValueTask<string?> ResolveAsync(string tenantId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (_cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAt > now)
        {
            cached.Touch(now);
            return cached.ConnectionString;
        }

        var tenant = await tenantStore.GetByIdAsync(tenantId, ct).ConfigureAwait(false);
        if (tenant is null)
            throw new TenantNotFoundException(tenantId);
        if (tenant.State == TenantState.Deactivated)
            throw new TenantDeactivatedException(tenantId);

        // Null when the tenant has no dedicated connection → caller keeps the shared/default database.
        var connectionString = tenant.ConnectionString;
        _cache[tenantId] = new CacheEntry(connectionString, now.Add(options.CacheTtl), now);

        if (_cache.Count > options.OptionsPoolSize)
            EvictLeastRecentlyUsed();

        return connectionString;
    }

    /// <summary>Returns the cached connection string for a warm tenant, or <c>null</c> when not cached.</summary>
    public string? ResolveCached(string tenantId)
        => _cache.TryGetValue(tenantId, out var e) && e.ExpiresAt > DateTimeOffset.UtcNow ? e.ConnectionString : null;

    /// <summary>Invalidates a tenant's cached connection (e.g. on a control-plane config change).</summary>
    public void InvalidateCache(string tenantId) => _cache.TryRemove(tenantId, out _);

    private void EvictLeastRecentlyUsed()
    {
        var snapshot = _cache.ToArray();
        var victimCount = Math.Max(1, snapshot.Length / 4);
        Array.Sort(snapshot, static (a, b) => a.Value.LastAccessTicks.CompareTo(b.Value.LastAccessTicks));
        for (var i = 0; i < victimCount; i++)
            _cache.TryRemove(snapshot[i].Key, out _);
    }

    private sealed class CacheEntry(string? connectionString, DateTimeOffset expiresAt, DateTimeOffset lastAccess)
    {
        public string? ConnectionString { get; } = connectionString;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public long LastAccessTicks = lastAccess.UtcTicks;
        public void Touch(DateTimeOffset when) => System.Threading.Interlocked.Exchange(ref LastAccessTicks, when.UtcTicks);
    }
}
