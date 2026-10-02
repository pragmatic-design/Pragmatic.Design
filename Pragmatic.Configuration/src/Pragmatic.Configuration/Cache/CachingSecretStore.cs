namespace Pragmatic.Configuration.Cache;

using Pragmatic.Caching;

/// <summary>
///     Read-through caching decorator over <see cref="ISecretStore" /> (the secret-store twin of
///     <see cref="CachingConfigurationStore" />). Reads are served from the <see cref="ICacheStack" />,
///     populated on miss, and invalidated on writes routed through <see cref="IWritableSecretStore" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>Rotation-aware TTL:</b> the cache never holds a secret past its advertised
///         <see cref="SecretEntry.ExpiresAt" /> — the entry TTL is <c>min(defaultTtl, ExpiresAt - now)</c>.
///         Not-found results get a shorter negative TTL. The default TTL is deliberately short (secrets
///         should not linger in memory as long as configuration).
///     </para>
///     <para>
///         This decorator is the single caching layer for secrets: backend stores (e.g. Key Vault) do not
///         cache themselves. It also implements <see cref="IWritableSecretStore" />, delegating writes to a
///         writable inner store and invalidating the affected key, so a write is immediately visible.
///     </para>
/// </remarks>
internal sealed class CachingSecretStore : ISecretStore, IWritableSecretStore
{
    private static readonly TimeSpan MaxNegativeTtl = TimeSpan.FromSeconds(30);

    private readonly ISecretStore _inner;
    private readonly ICacheStack _cache;
    private readonly TimeSpan _ttl;

    public CachingSecretStore(ISecretStore inner, ICacheStack cache, TimeSpan ttl)
    {
        _inner = inner;
        _cache = cache;
        _ttl = ttl <= TimeSpan.Zero ? TimeSpan.FromSeconds(60) : ttl;
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        => (await GetEntryAsync(key, null, ct).ConfigureAwait(false)).Value;

    public async Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
        => (await GetEntryAsync(key, tenantId, ct).ConfigureAwait(false)).Value;

    public async Task<SecretEntry> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
        => await GetEntryAsync(key, null, ct).ConfigureAwait(false);

    public async Task<SecretEntry> GetSecretWithMetadataAsync(string key, string tenantId, CancellationToken ct = default)
        => await GetEntryAsync(key, tenantId, ct).ConfigureAwait(false);

    public async Task SetSecretAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        await Writable().SetSecretAsync(key, value, tenantId, ct).ConfigureAwait(false);
        await InvalidateAsync(key, tenantId, ct).ConfigureAwait(false);
    }

    public async Task DeleteSecretAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        await Writable().DeleteSecretAsync(key, tenantId, ct).ConfigureAwait(false);
        await InvalidateAsync(key, tenantId, ct).ConfigureAwait(false);
    }

    private async Task<SecretEntry> GetEntryAsync(string key, string? tenantId, CancellationToken ct)
    {
        var cacheKey = ValueKey(key, tenantId);

        var (found, cached) = await _cache.TryGetAsync<SecretEntry>(cacheKey, ct).ConfigureAwait(false);
        if (found && cached is not null)
            return cached;

        // Always fetch WITH metadata: it gives the expiry that drives the rotation-aware TTL, and for
        // stores that expose it (Key Vault) it is a single round-trip anyway.
        var entry = tenantId is null
            ? await _inner.GetSecretWithMetadataAsync(key, ct).ConfigureAwait(false)
            : await _inner.GetSecretWithMetadataAsync(key, tenantId, ct).ConfigureAwait(false);

        var ttl = CacheTtl(entry);
        if (ttl > TimeSpan.Zero)
            await _cache.SetAsync(cacheKey, entry, Options(key, tenantId, ttl), ct).ConfigureAwait(false);

        return entry;
    }

    private IWritableSecretStore Writable()
        => _inner as IWritableSecretStore
           ?? throw new NotSupportedException(
               $"The underlying secret store '{_inner.GetType().Name}' is read-only and does not implement IWritableSecretStore.");

    private async Task InvalidateAsync(string key, string? tenantId, CancellationToken ct)
        => await _cache.InvalidateByTagsAsync([KeyTag(key, tenantId), ScopeTag(tenantId)], ct).ConfigureAwait(false);

    // Never cache past the advertised expiry; negative (not-found) results get a shorter TTL.
    private TimeSpan CacheTtl(SecretEntry entry)
    {
        var ttl = entry.Found
            ? _ttl
            : TimeSpan.FromSeconds(Math.Min(_ttl.TotalSeconds, MaxNegativeTtl.TotalSeconds));

        if (entry.ExpiresAt is { } expiresAt)
        {
            var untilExpiry = expiresAt - DateTimeOffset.UtcNow;
            if (untilExpiry < ttl)
                ttl = untilExpiry;
        }

        return ttl;
    }

    private static string ValueKey(string key, string? tenantId) => $"sec:v:{tenantId ?? string.Empty}:{key}";
    private static string KeyTag(string key, string? tenantId) => $"sec-key:{tenantId ?? string.Empty}:{key}";
    private static string ScopeTag(string? tenantId) => $"sec-scope:{tenantId ?? string.Empty}";

    private static CacheEntryOptions Options(string key, string? tenantId, TimeSpan ttl) => new()
    {
        Duration = ttl,
        Tags = [KeyTag(key, tenantId), ScopeTag(tenantId)],
    };
}
