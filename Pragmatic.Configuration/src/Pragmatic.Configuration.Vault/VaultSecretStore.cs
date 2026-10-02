using System.Net;
using VaultSharp;
using VaultSharp.Core;

namespace Pragmatic.Configuration.Vault;

/// <summary>
///     <see cref="ISecretStore" /> (and <see cref="IWritableSecretStore" />) backed by HashiCorp Vault's KV v2
///     engine. Each secret is stored at <c>{prefix}/{key}</c> (base) or <c>{prefix}/tenants/{tenant}/{key}</c>
///     with a single <c>value</c> field. Vault owns encryption, versioning, and access control.
/// </summary>
public sealed class VaultSecretStore(IVaultClient client, VaultConfigurationOptions options)
    : ISecretStore, IWritableSecretStore
{
    private const string ValueField = "value";

    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        => ReadAsync(Path(key, null));

    public Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
        => ReadAsync(Path(key, tenantId));

    public async Task SetSecretAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
        => await client.V1.Secrets.KeyValue.V2.WriteSecretAsync(
                Path(key, tenantId),
                new Dictionary<string, object> { [ValueField] = value },
                mountPoint: options.MountPoint)
            .ConfigureAwait(false);

    public async Task DeleteSecretAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        try
        {
            // Permanently removes every version + metadata — the delete contract is "gone", not "soft".
            await client.V1.Secrets.KeyValue.V2.DeleteMetadataAsync(Path(key, tenantId), mountPoint: options.MountPoint)
                .ConfigureAwait(false);
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // Idempotent: deleting a missing secret is a no-op.
        }
    }

    private async Task<string?> ReadAsync(string path)
    {
        try
        {
            var secret = await client.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(path, mountPoint: options.MountPoint).ConfigureAwait(false);

            return secret.Data.Data.TryGetValue(ValueField, out var value) ? value?.ToString() : null;
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private string Path(string key, string? tenantId)
    {
        var prefix = string.IsNullOrEmpty(options.PathPrefix)
            ? string.Empty
            : options.PathPrefix!.Trim('/') + "/";

        return tenantId is null
            ? $"{prefix}{key}"
            : $"{prefix}tenants/{tenantId}/{key}";
    }
}
