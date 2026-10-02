using System.Text;
using global::Consul;

namespace Pragmatic.Configuration.Consul;

/// <summary>
///     <see cref="IConfigurationStore" /> backed by the HashiCorp Consul KV store. Keys live at
///     <c>{prefix}/{key}</c> (base) or <c>{prefix}/tenants/{tenant}/{key}</c>. Consul has a native blocking-query
///     watch, but this backend does not surface it yet, so <see cref="WatchAsync" /> yields nothing.
/// </summary>
public sealed class ConsulConfigurationStore(IConsulClient client, ConsulConfigurationOptions options)
    : IConfigurationStore
{
    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => ReadAsync(FullKey(key, null), ct);

    public Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
        => ReadAsync(FullKey(key, tenantId), ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
        => ReadSectionAsync(prefix, null, ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(
        string prefix, string tenantId, CancellationToken ct = default)
        => ReadSectionAsync(prefix, tenantId, ct);

    public async Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
        => await client.KV.Put(
                new KVPair(FullKey(key, tenantId)) { Value = Encoding.UTF8.GetBytes(value) }, ct)
            .ConfigureAwait(false);

    public async Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
        => await client.KV.Delete(FullKey(key, tenantId), ct).ConfigureAwait(false);

    public IAsyncEnumerable<ConfigurationChange> WatchAsync(string keyPattern, CancellationToken ct = default)
        => EmptyStream();

    private async Task<string?> ReadAsync(string fullKey, CancellationToken ct)
    {
        var result = await client.KV.Get(fullKey, ct).ConfigureAwait(false);
        return result.Response is { Value: { } bytes } ? Encoding.UTF8.GetString(bytes) : null;
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadSectionAsync(
        string prefix, string? tenantId, CancellationToken ct)
    {
        var basePath = BasePath(tenantId);
        var listPrefix = basePath + prefix;

        var result = await client.KV.List(listPrefix, ct).ConfigureAwait(false);
        var section = new Dictionary<string, string>();

        if (result.Response is { } pairs)
            foreach (var pair in pairs)
            {
                if (pair.Value is null)
                    continue;

                var logicalKey = pair.Key.StartsWith(basePath, StringComparison.Ordinal)
                    ? pair.Key[basePath.Length..]
                    : pair.Key;
                section[logicalKey] = Encoding.UTF8.GetString(pair.Value);
            }

        return section;
    }

    private static async IAsyncEnumerable<ConfigurationChange> EmptyStream()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <summary>The prefix path (with trailing slash) before a logical key, honoring the tenant nesting.</summary>
    private string BasePath(string? tenantId)
    {
        var prefix = string.IsNullOrEmpty(options.KeyPrefix) ? string.Empty : options.KeyPrefix!.Trim('/') + "/";
        return tenantId is null ? prefix : $"{prefix}tenants/{tenantId}/";
    }

    private string FullKey(string key, string? tenantId) => BasePath(tenantId) + key;
}
