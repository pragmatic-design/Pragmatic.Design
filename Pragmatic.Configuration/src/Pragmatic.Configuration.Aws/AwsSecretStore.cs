using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace Pragmatic.Configuration.Aws;

/// <summary>
///     <see cref="ISecretStore" /> (and <see cref="IWritableSecretStore" />) backed by AWS Secrets Manager.
///     Secret names are <c>{prefix}/{key}</c> (base) or <c>{prefix}/tenants/{tenant}/{key}</c>, with the
///     Pragmatic <c>:</c> separator mapped to <c>/</c>. AWS owns encryption and rotation.
/// </summary>
public sealed class AwsSecretStore(IAmazonSecretsManager client, AwsConfigurationOptions options)
    : ISecretStore, IWritableSecretStore
{
    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
        => ReadAsync(Name(key, null), ct);

    public Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
        => ReadAsync(Name(key, tenantId), ct);

    public async Task SetSecretAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        var name = Name(key, tenantId);
        try
        {
            await client.PutSecretValueAsync(
                new PutSecretValueRequest { SecretId = name, SecretString = value }, ct).ConfigureAwait(false);
        }
        catch (ResourceNotFoundException)
        {
            // First write for this name — create it.
            await client.CreateSecretAsync(
                new CreateSecretRequest { Name = name, SecretString = value }, ct).ConfigureAwait(false);
        }
    }

    public async Task DeleteSecretAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        try
        {
            // Force-delete (no recovery window) so a later create of the same name is not blocked.
            await client.DeleteSecretAsync(
                new DeleteSecretRequest { SecretId = Name(key, tenantId), ForceDeleteWithoutRecovery = true }, ct)
                .ConfigureAwait(false);
        }
        catch (ResourceNotFoundException)
        {
            // Idempotent: deleting a missing secret is a no-op.
        }
    }

    private async Task<string?> ReadAsync(string name, CancellationToken ct)
    {
        try
        {
            var response = await client.GetSecretValueAsync(
                new GetSecretValueRequest { SecretId = name }, ct).ConfigureAwait(false);
            return response.SecretString;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }

    private string Name(string key, string? tenantId) => AwsNaming.SecretName(options.PathPrefix, tenantId, key);
}
