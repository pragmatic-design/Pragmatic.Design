using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.ConnectionString;

namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Resolves connection strings per-tenant from <see cref="ITenantStore" />.
///     Tenants with a dedicated <see cref="TenantInfo.ConnectionString" /> get their own
///     database. Tenants without one use the
///     <see cref="TenantDatabaseOptions.DefaultConnectionString" /> (row-level isolation).
/// </summary>
public sealed class TenantConnectionStringProvider<TDbContext>(
    ITenantContext tenantContext,
    ITenantStore tenantStore,
    TenantDatabaseOptions options) : IConnectionStringProvider<TDbContext>
    where TDbContext : DbContext
{
    // Per-tenant cache: tenantId → (connectionString, expiresAt, lastAccess).
    // Case-insensitive comparer so "Acme" and "acme" hit the same entry.
    // Eviction policy: TTL on read (expired entries are ignored and refreshed),
    // and least-recently-used (oldest LastAccess) on overflow.
    // Instance-level (not static) so each registered provider instance has its own isolated cache,
    // which ensures test isolation and avoids cross-scope contamination.
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async ValueTask<string> GetConnectionStringAsync(CancellationToken ct = default)
    {
        if (!tenantContext.IsResolved)
        {
            if (string.IsNullOrEmpty(options.DefaultConnectionString))
                throw new InvalidOperationException(
                    $"No tenant is resolved and {nameof(TenantDatabaseOptions)}.{nameof(TenantDatabaseOptions.DefaultConnectionString)} is not configured.");
            return options.DefaultConnectionString;
        }

        var tenantId = tenantContext.TenantId!;
        var now = DateTimeOffset.UtcNow;

        // Cache hit: refresh LastAccess so this entry is "freshly used" for LRU.
        if (_cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAt > now)
        {
            cached.Touch(now);
            return cached.ConnectionString;
        }

        // Resolve from store
        var tenant = await tenantStore.GetByIdAsync(tenantId, ct).ConfigureAwait(false);

        if (tenant is null)
            throw new TenantNotFoundException(tenantId);

        if (tenant.State == TenantState.Deactivated)
            throw new TenantDeactivatedException(tenantId);

        var connectionString = tenant.ConnectionString ?? options.DefaultConnectionString;
        _cache[tenantId] = new CacheEntry(connectionString, now.Add(options.CacheTtl), now);

        // Count check and eviction are best-effort: under concurrent load, capacity may temporarily
        // exceed OptionsPoolSize by the number of concurrent writers before eviction completes.
        // This is acceptable for a TTL cache — correctness is not affected.
        if (_cache.Count > options.OptionsPoolSize)
            EvictLeastRecentlyUsed();

        return connectionString;
    }

    /// <summary>
    ///     Invalidates the cached connection string for a tenant. Call when tenant
    ///     configuration changes (e.g. via control-plane push).
    /// </summary>
    public void InvalidateCache(string tenantId)
        => _cache.TryRemove(tenantId, out _);

    /// <summary>Clears the entire connection string cache.</summary>
    public void ClearCache() => _cache.Clear();

    private void EvictLeastRecentlyUsed()
    {
        // Take a point-in-time snapshot of the keys sorted by LastAccessTicks to avoid holding
        // the enumerator while mutating the dictionary.  Drop the bottom quartile by last access.
        var snapshot = _cache.ToArray();           // O(n) snapshot — safe to sort without contention
        var victimCount = Math.Max(1, snapshot.Length / 4);
        Array.Sort(snapshot, static (a, b) => a.Value.LastAccessTicks.CompareTo(b.Value.LastAccessTicks));

        for (var i = 0; i < victimCount; i++)
            _cache.TryRemove(snapshot[i].Key, out _);
    }

    private sealed class CacheEntry(string connectionString, DateTimeOffset expiresAt, DateTimeOffset lastAccess)
    {
        public string ConnectionString { get; } = connectionString;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        // Updated atomically on each cache hit; read by EvictLeastRecentlyUsed.
        // Stored as ticks so we can use Interlocked on long.
        public long LastAccessTicks = lastAccess.UtcTicks;

        public void Touch(DateTimeOffset when) =>
            System.Threading.Interlocked.Exchange(ref LastAccessTicks, when.UtcTicks);
    }
}
