namespace Pragmatic.Configuration.Kubernetes;

/// <summary>
///     <see cref="ISecretStore" /> (and <see cref="IWritableSecretStore" />) backed by a Kubernetes Secret
///     (one object per tenant). Kubernetes owns encoding and RBAC.
/// </summary>
public sealed class KubernetesSecretStore : ISecretStore, IWritableSecretStore
{
    private readonly KubernetesBagStore _bag;

    internal KubernetesSecretStore(IKubernetesBagApi api, KubernetesConfigurationOptions options)
        => _bag = new KubernetesBagStore(api, options.ObjectName);

    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        => _bag.GetAsync(key, null, ct);

    public Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
        => _bag.GetAsync(key, tenantId, ct);

    public Task SetSecretAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
        => _bag.SetAsync(key, value, tenantId, ct);

    public Task DeleteSecretAsync(string key, string? tenantId = null, CancellationToken ct = default)
        => _bag.DeleteAsync(key, tenantId, ct);
}
