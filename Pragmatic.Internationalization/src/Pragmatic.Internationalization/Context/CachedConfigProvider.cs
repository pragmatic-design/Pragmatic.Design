using System.Collections.Concurrent;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Base class for configuration providers that need to cache their configuration.
/// </summary>
/// <remarks>
///     <para>
///         Use this base class when your provider loads configuration from external sources
///         (database, API, etc.) and you want to avoid querying the source on every request.
///     </para>
///     <para>
///         The cache is automatically invalidated after the specified duration.
///         Call <see cref="InvalidateCache()"/> to manually invalidate when settings change.
///     </para>
/// </remarks>
/// <example>
/// <code>
/// public class TenantConfigProvider(ITenantContext tenant, IDbContext db)
///     : CachedConfigProvider(TimeSpan.FromMinutes(5))
/// {
///     public override int Priority => 100;
///
///     protected override string? GetCacheKey()
///     {
///         var tenantId = tenant.Current?.Id;
///         return tenantId is null ? null : $"i18n:tenant:{tenantId}";
///     }
///
///     protected override I18NConfig? LoadConfiguration()
///     {
///         var settings = db.TenantSettings.FirstOrDefault(s => s.TenantId == tenant.Current.Id);
///         if (settings is null) return null;
///
///         return new I18NConfig
///         {
///             DefaultUICulture = CultureCode.FromString(settings.DefaultCulture),
///             SupportedCultures = settings.SupportedCultures.Select(CultureCode.FromString).ToList()
///         };
///     }
/// }
/// </code>
/// </example>
public abstract class CachedConfigProvider : II18NConfigProvider
{
    // Cache shared across all instances of the SAME concrete provider type. Config providers
    // are typically registered scoped (they inject scoped ITenantContext/DbContext), so an
    // instance-level cache would be recreated empty every request and the TTL would never take
    // effect — LoadConfiguration() (the DB hit) would run on every request. Keying by GetType()
    // keeps different provider types isolated; cross-tenant isolation is preserved because
    // GetCacheKey() already encodes the tenant/user identity.
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, CacheEntry>> SharedCaches = new();

    private readonly ConcurrentDictionary<string, CacheEntry> _cache;
    private readonly TimeSpan _cacheDuration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CachedConfigProvider"/> class.
    /// </summary>
    /// <param name="cacheDuration">
    ///     The duration to cache the configuration. Defaults to 5 minutes.
    /// </param>
    protected CachedConfigProvider(TimeSpan? cacheDuration = null)
    {
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
        _cache = SharedCaches.GetOrAdd(GetType(), static _ => new ConcurrentDictionary<string, CacheEntry>());
    }

    /// <inheritdoc />
    public abstract int Priority { get; }

    /// <summary>
    ///     Gets the cache key for this provider.
    /// </summary>
    /// <remarks>
    ///     Return null to skip caching (e.g., when there's no valid context).
    ///     The key should be unique per configuration source (e.g., include tenant ID).
    /// </remarks>
    /// <returns>The cache key, or null to skip caching.</returns>
    protected abstract string? GetCacheKey();

    /// <summary>
    ///     Loads the configuration from the external source.
    /// </summary>
    /// <remarks>
    ///     This method is called when the cache is empty or expired.
    ///     Return null if this provider has no configuration (defers to lower priority).
    /// </remarks>
    /// <returns>The configuration, or null to defer.</returns>
    protected abstract I18NConfig? LoadConfiguration();

    /// <inheritdoc />
    public I18NConfig? GetConfiguration()
    {
        var cacheKey = GetCacheKey();

        // Skip caching if no key (e.g., no tenant context)
        if (string.IsNullOrEmpty(cacheKey))
            return LoadConfiguration();

        // Try to get a non-expired entry from cache
        if (_cache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
            return cached.Config;

        // Load configuration and atomically store it.
        // Concurrent first-requests may both call LoadConfiguration(), but only one
        // entry survives in the dictionary; the cost is bounded (each call loads once)
        // and is safe because LoadConfiguration() is idempotent.
        var config = LoadConfiguration();
        var expiration = DateTime.UtcNow.Add(_cacheDuration);
        var entry = new CacheEntry(config, expiration);

        // Remove stale expired entry first so the new entry can be inserted atomically
        if (cached is not null)
            _cache.TryUpdate(cacheKey, entry, cached);
        else
            _cache.TryAdd(cacheKey, entry);

        // Cleanup expired entries periodically (every 100 accesses on average)
        if (Random.Shared.Next(100) == 0)
            CleanupExpiredEntries();

        return config;
    }

    /// <summary>
    ///     Invalidates the cached configuration.
    /// </summary>
    /// <remarks>
    ///     Call this method when the underlying configuration changes
    ///     (e.g., when tenant or user settings are updated).
    /// </remarks>
    public void InvalidateCache()
    {
        var cacheKey = GetCacheKey();
        if (!string.IsNullOrEmpty(cacheKey))
            _cache.TryRemove(cacheKey, out _);
    }

    /// <summary>
    ///     Invalidates a specific cache key.
    /// </summary>
    /// <param name="cacheKey">The cache key to invalidate.</param>
    /// <remarks>
    ///     Use this when you need to invalidate a key that's different from the current context
    ///     (e.g., invalidating a specific tenant's cache after an admin update).
    /// </remarks>
    public void InvalidateCache(string cacheKey)
    {
        if (!string.IsNullOrEmpty(cacheKey))
            _cache.TryRemove(cacheKey, out _);
    }

    /// <summary>
    ///     Clears all cached configurations.
    /// </summary>
    /// <remarks>
    ///     Use with caution in production. Primarily useful for testing.
    /// </remarks>
    public void ClearAllCache()
    {
        _cache.Clear();
    }

    private void CleanupExpiredEntries()
    {
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
            _cache.TryRemove(key, out _);
    }

    /// <summary>
    ///     Cache entry with expiration tracking.
    /// </summary>
    private sealed record CacheEntry(I18NConfig? Config, DateTime Expiration)
    {
        public bool IsExpired => DateTime.UtcNow > Expiration;
    }
}
