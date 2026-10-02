using StackExchange.Redis;

namespace Pragmatic.Configuration.Redis;

/// <summary>
///     <see cref="IConfigurationStore" /> backed by Redis strings. Keys live at <c>{prefix}:{key}</c> (base) or
///     <c>{prefix}:tenants:{tenant}:{key}</c>. Section reads use a <c>SCAN MATCH</c> over the prefix. Redis
///     keyspace notifications are not wired, so <see cref="WatchAsync" /> yields nothing.
/// </summary>
public sealed class RedisConfigurationStore(IConnectionMultiplexer multiplexer, RedisConfigurationOptions options)
    : IConfigurationStore
{
    private IDatabase Db => multiplexer.GetDatabase();

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var value = await Db.StringGetAsync(FullKey(key, null)).ConfigureAwait(false);
        return value.IsNull ? null : value.ToString();
    }

    public async Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
    {
        var value = await Db.StringGetAsync(FullKey(key, tenantId)).ConfigureAwait(false);
        return value.IsNull ? null : value.ToString();
    }

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
        => ReadSectionAsync(prefix, null, ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(
        string prefix, string tenantId, CancellationToken ct = default)
        => ReadSectionAsync(prefix, tenantId, ct);

    public async Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
        => await Db.StringSetAsync(FullKey(key, tenantId), value).ConfigureAwait(false);

    public async Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
        => await Db.KeyDeleteAsync(FullKey(key, tenantId)).ConfigureAwait(false);

    public IAsyncEnumerable<ConfigurationChange> WatchAsync(string keyPattern, CancellationToken ct = default)
        => EmptyStream();

    private async Task<IReadOnlyDictionary<string, string>> ReadSectionAsync(
        string prefix, string? tenantId, CancellationToken ct)
    {
        var basePath = BasePath(tenantId);
        var pattern = $"{basePath}{prefix}*";
        var section = new Dictionary<string, string>();

        // KeysAsync issues a SCAN (cursor-based, non-blocking) across the server's keyspace.
        var server = multiplexer.GetServer(multiplexer.GetEndPoints()[0]);
        await foreach (var redisKey in server.KeysAsync(pattern: pattern).WithCancellation(ct).ConfigureAwait(false))
        {
            var value = await Db.StringGetAsync(redisKey).ConfigureAwait(false);
            if (value.IsNull)
                continue;

            var full = redisKey.ToString();
            var logicalKey = full.StartsWith(basePath, StringComparison.Ordinal) ? full[basePath.Length..] : full;
            section[logicalKey] = value.ToString();
        }

        return section;
    }

    private static async IAsyncEnumerable<ConfigurationChange> EmptyStream()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <summary>The prefix (with trailing colon) before a logical key, honoring the tenant nesting.</summary>
    private string BasePath(string? tenantId)
    {
        var prefix = string.IsNullOrEmpty(options.KeyPrefix) ? string.Empty : options.KeyPrefix!.Trim(':') + ":";
        return tenantId is null ? prefix : $"{prefix}tenants:{tenantId}:";
    }

    private string FullKey(string key, string? tenantId) => BasePath(tenantId) + key;
}
