namespace Pragmatic.Configuration.Cache;

using System.Runtime.CompilerServices;
using Pragmatic.Caching;

/// <summary>
///     Read-through caching decorator over <see cref="IConfigurationStore" />.
///     Reads (<c>Get</c>/<c>GetSection</c>) are served from the <see cref="ICacheStack" /> when present,
///     populated on miss, and invalidated on writes (<c>Set</c>/<c>Delete</c>) and on changes surfaced
///     by <see cref="WatchAsync" />.
/// </summary>
/// <remarks>
///     <para>
///         Cache keys are scoped by environment and tenant so values for different environments or
///         tenants never collide (see <see cref="ValueKey" /> / <see cref="SectionKey" />).
///     </para>
///     <para>
///         Invalidation uses tags. Every value entry carries a per-key tag and a per-scope tag; every
///         section entry carries the per-scope tag. A write to a single key invalidates that key's value
///         entry plus every cached section in the same scope (a section result may contain the key, and
///         the prefix relationship cannot be tested cheaply at invalidation time).
///     </para>
/// </remarks>
internal sealed class CachingConfigurationStore(IConfigurationStore inner, ICacheStack cache, EnvironmentProfile environment)
    : IConfigurationStore
{
    private readonly string _env = environment.Name;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
        => await GetValueAsync(key, null, ct).ConfigureAwait(false);

    public async Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
        => await GetValueAsync(key, tenantId, ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
        => await GetSectionInternalAsync(prefix, null, ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default)
        => await GetSectionInternalAsync(prefix, tenantId, ct).ConfigureAwait(false);

    public async Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        await inner.SetAsync(key, value, tenantId, ct).ConfigureAwait(false);
        await InvalidateKeyAsync(key, tenantId, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        await inner.DeleteAsync(key, tenantId, ct).ConfigureAwait(false);
        await InvalidateKeyAsync(key, tenantId, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ConfigurationChange> WatchAsync(
        string keyPattern,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var change in inner.WatchAsync(keyPattern, ct).ConfigureAwait(false))
        {
            // A watched change makes any cached read for that key (and its scope's sections) stale.
            await InvalidateKeyAsync(change.Key, change.TenantId, ct).ConfigureAwait(false);
            yield return change;
        }
    }

    private async Task<string?> GetValueAsync(string key, string? tenantId, CancellationToken ct)
    {
        var cacheKey = ValueKey(key, tenantId);

        var (found, cached) = await cache.TryGetAsync<string?>(cacheKey, ct).ConfigureAwait(false);
        if (found)
            return cached;

        var value = tenantId is null
            ? await inner.GetAsync(key, ct).ConfigureAwait(false)
            : await inner.GetAsync(key, tenantId, ct).ConfigureAwait(false);

        await cache.SetAsync(cacheKey, value, ValueOptions(key, tenantId), ct).ConfigureAwait(false);
        return value;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetSectionInternalAsync(
        string prefix, string? tenantId, CancellationToken ct)
    {
        var cacheKey = SectionKey(prefix, tenantId);

        var (found, cached) = await cache.TryGetAsync<IReadOnlyDictionary<string, string>>(cacheKey, ct).ConfigureAwait(false);
        if (found && cached is not null)
            return cached;

        var section = tenantId is null
            ? await inner.GetSectionAsync(prefix, ct).ConfigureAwait(false)
            : await inner.GetSectionAsync(prefix, tenantId, ct).ConfigureAwait(false);

        await cache.SetAsync(cacheKey, section, SectionOptions(tenantId), ct).ConfigureAwait(false);
        return section;
    }

    private async Task InvalidateKeyAsync(string key, string? tenantId, CancellationToken ct)
        => await cache.InvalidateByTagsAsync([KeyTag(key, tenantId), ScopeTag(tenantId)], ct).ConfigureAwait(false);

    // Cache-key scoping: env + tenant ensures no cross-environment / cross-tenant collisions.
    private string ValueKey(string key, string? tenantId) => $"cfg:v:{_env}:{tenantId ?? string.Empty}:{key}";

    private string SectionKey(string prefix, string? tenantId) => $"cfg:s:{_env}:{tenantId ?? string.Empty}:{prefix}";

    private string KeyTag(string key, string? tenantId) => $"cfg-key:{_env}:{tenantId ?? string.Empty}:{key}";

    private string ScopeTag(string? tenantId) => $"cfg-scope:{_env}:{tenantId ?? string.Empty}";

    private CacheEntryOptions ValueOptions(string key, string? tenantId) => new()
    {
        Tags = [KeyTag(key, tenantId), ScopeTag(tenantId)],
    };

    private CacheEntryOptions SectionOptions(string? tenantId) => new()
    {
        Tags = [ScopeTag(tenantId)],
    };
}
