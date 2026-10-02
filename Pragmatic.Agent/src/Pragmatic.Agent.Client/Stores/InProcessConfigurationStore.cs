using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Pragmatic.Configuration;

namespace Pragmatic.Agent.Client.Stores;

/// <summary>
///     Minimal in-process <see cref="IConfigurationStore"/> used as the local fallback for
///     <see cref="AgentConfigurationStore"/> when the Agent daemon is unreachable (L0 mode).
///     Agent.Client only depends on Pragmatic.Abstractions, so it cannot use the full
///     InMemoryConfigurationStore from Pragmatic.Configuration; this keeps L0 config functional
///     (reads/writes persist locally) instead of silently no-opping.
/// </summary>
/// <remarks>
///     Tenant-scoped reads return only the tenant's overrides (or null / empty), matching the
///     IConfigurationStore contract — the cascade to base is the resolver's job. Watch is a no-op
///     (no hot-reload while degraded).
/// </remarks>
internal sealed class InProcessConfigurationStore : IConfigurationStore
{
    private readonly ConcurrentDictionary<string, string> _base = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _tenants = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        _base.TryGetValue(key, out var value);
        return Task.FromResult<string?>(value);
    }

    public Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
    {
        if (_tenants.TryGetValue(tenantId, out var dict) && dict.TryGetValue(key, out var value))
            return Task.FromResult<string?>(value);
        return Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
    {
        var result = _base
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_tenants.TryGetValue(tenantId, out var dict))
            foreach (var kv in dict.Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                result[kv.Key] = kv.Value;
        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    public Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        if (tenantId is null)
            _base[key] = value;
        else
            _tenants.GetOrAdd(tenantId, _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal))[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        if (tenantId is null)
            _base.TryRemove(key, out _);
        else if (_tenants.TryGetValue(tenantId, out var dict))
            dict.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ConfigurationChange> WatchAsync(
        string keyPattern, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // No change stream while degraded to the in-process fallback.
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
