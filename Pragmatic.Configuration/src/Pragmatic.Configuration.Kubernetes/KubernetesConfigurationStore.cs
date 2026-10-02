namespace Pragmatic.Configuration.Kubernetes;

/// <summary>
///     <see cref="IConfigurationStore" /> backed by a Kubernetes ConfigMap (one object per tenant). Config maps
///     have no native change stream wired here, so <see cref="WatchAsync" /> yields nothing.
/// </summary>
public sealed class KubernetesConfigurationStore : IConfigurationStore
{
    private readonly KubernetesBagStore _bag;

    internal KubernetesConfigurationStore(IKubernetesBagApi api, KubernetesConfigurationOptions options)
        => _bag = new KubernetesBagStore(api, options.ObjectName);

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => _bag.GetAsync(key, null, ct);

    public Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
        => _bag.GetAsync(key, tenantId, ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
        => _bag.GetSectionAsync(prefix, null, ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(
        string prefix, string tenantId, CancellationToken ct = default)
        => _bag.GetSectionAsync(prefix, tenantId, ct);

    public Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
        => _bag.SetAsync(key, value, tenantId, ct);

    public Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
        => _bag.DeleteAsync(key, tenantId, ct);

    public IAsyncEnumerable<ConfigurationChange> WatchAsync(string keyPattern, CancellationToken ct = default)
        => EmptyStream();

    private static async IAsyncEnumerable<ConfigurationChange> EmptyStream()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
