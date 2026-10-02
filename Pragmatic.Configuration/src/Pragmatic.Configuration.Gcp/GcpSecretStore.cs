namespace Pragmatic.Configuration.Gcp;

/// <summary>
///     <see cref="ISecretStore" /> (and <see cref="IWritableSecretStore" />) backed by GCP Secret Manager.
///     Logical keys map to flat secret ids via <see cref="GcpSecretName" />; GCP owns encryption, versioning,
///     and IAM.
/// </summary>
public sealed class GcpSecretStore : ISecretStore, IWritableSecretStore
{
    private readonly IGcpSecretApi _api;
    private readonly GcpConfigurationOptions _options;

    /// <summary>Constructs the store over the real Secret Manager client.</summary>
    public GcpSecretStore(
        global::Google.Cloud.SecretManager.V1.SecretManagerServiceClient client, GcpConfigurationOptions options)
        : this(new GcpSecretApi(client, options.ProjectId), options)
    {
    }

    internal GcpSecretStore(IGcpSecretApi api, GcpConfigurationOptions options)
    {
        _api = api;
        _options = options;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        => _api.AccessLatestAsync(Name(key, null), ct);

    public Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
        => _api.AccessLatestAsync(Name(key, tenantId), ct);

    public Task SetSecretAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
        => _api.UpsertAsync(Name(key, tenantId), value, ct);

    public Task DeleteSecretAsync(string key, string? tenantId = null, CancellationToken ct = default)
        => _api.DeleteAsync(Name(key, tenantId), ct);

    private string Name(string key, string? tenantId) => GcpSecretName.Build(_options.Prefix, tenantId, key);
}
