namespace Pragmatic.Configuration.Kubernetes;

/// <summary>
///     Core read-modify-write logic shared by the ConfigMap-backed configuration store and the Secret-backed
///     secret store: a logical key maps to a data entry in a per-tenant object.
/// </summary>
internal sealed class KubernetesBagStore(IKubernetesBagApi api, string baseName)
{
    public async Task<string?> GetAsync(string key, string? tenantId, CancellationToken ct)
    {
        var data = await api.ReadAsync(ObjectName(tenantId), ct).ConfigureAwait(false);
        return data is not null && data.TryGetValue(KubernetesNaming.DataKey(key), out var value) ? value : null;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSectionAsync(
        string prefix, string? tenantId, CancellationToken ct)
    {
        var data = await api.ReadAsync(ObjectName(tenantId), ct).ConfigureAwait(false);
        var result = new Dictionary<string, string>();

        if (data is not null)
            foreach (var (dataKey, value) in data)
            {
                var logicalKey = KubernetesNaming.LogicalKey(dataKey);
                if (logicalKey.StartsWith(prefix, StringComparison.Ordinal))
                    result[logicalKey] = value;
            }

        return result;
    }

    public async Task SetAsync(string key, string value, string? tenantId, CancellationToken ct)
    {
        var name = ObjectName(tenantId);
        var current = await api.ReadAsync(name, ct).ConfigureAwait(false);
        var data = current is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(current);

        data[KubernetesNaming.DataKey(key)] = value;
        await api.UpsertAsync(name, data, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, string? tenantId, CancellationToken ct)
    {
        var name = ObjectName(tenantId);
        var current = await api.ReadAsync(name, ct).ConfigureAwait(false);
        var dataKey = KubernetesNaming.DataKey(key);
        if (current is null || !current.ContainsKey(dataKey))
            return; // Idempotent.

        var data = new Dictionary<string, string>(current);
        data.Remove(dataKey);
        await api.UpsertAsync(name, data, ct).ConfigureAwait(false);
    }

    private string ObjectName(string? tenantId) => KubernetesNaming.ObjectName(baseName, tenantId);
}
