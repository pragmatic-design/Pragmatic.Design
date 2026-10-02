using System.Collections.Concurrent;

namespace Pragmatic.Configuration.Management.Tests.Unit;

/// <summary>
///     Hand-rolled <see cref="IConfigurationStore"/> fake that records the exact
///     overload/arguments each action invokes, so delegation can be asserted
///     without coupling to a real backend.
/// </summary>
internal sealed class RecordingConfigurationStore : IConfigurationStore
{
    private readonly ConcurrentDictionary<string, string> _base = new();
    private readonly ConcurrentDictionary<(string Tenant, string Key), string> _tenant = new();

    public List<(string Key, string? TenantId)> GetCalls { get; } = [];
    public List<(string Prefix, string? TenantId)> SectionCalls { get; } = [];
    public List<(string Key, string Value, string? TenantId)> SetCalls { get; } = [];
    public List<(string Key, string? TenantId)> DeleteCalls { get; } = [];

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        GetCalls.Add((key, null));
        _base.TryGetValue(key, out var v);
        return Task.FromResult(v);
    }

    public Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
    {
        GetCalls.Add((key, tenantId));
        _tenant.TryGetValue((tenantId, key), out var v);
        return Task.FromResult(v);
    }

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
    {
        SectionCalls.Add((prefix, null));
        IReadOnlyDictionary<string, string> result = new Dictionary<string, string>(_base);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default)
    {
        SectionCalls.Add((prefix, tenantId));
        var dict = _tenant
            .Where(kv => kv.Key.Tenant == tenantId)
            .ToDictionary(kv => kv.Key.Key, kv => kv.Value);
        IReadOnlyDictionary<string, string> result = dict;
        return Task.FromResult(result);
    }

    public Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        SetCalls.Add((key, value, tenantId));
        if (tenantId is null)
            _base[key] = value;
        else
            _tenant[(tenantId, key)] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        DeleteCalls.Add((key, tenantId));
        if (tenantId is null)
            _base.TryRemove(key, out _);
        else
            _tenant.TryRemove((tenantId, key), out _);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ConfigurationChange> WatchAsync(
        string keyPattern,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
