using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Cryptography;

namespace Pragmatic.Configuration.Database.Encryption;

/// <summary>
///     <see cref="IEncryptionKeyProvider" /> that resolves the base64-encoded AES-256 key from an
///     <see cref="ISecretStore" /> by a configured secret name. This is the path that integrates with the
///     module's own secret-store abstraction: the database store's encryption key can itself live in an
///     external vault (e.g. <c>AzureKeyVaultSecretStore</c>) accessed via managed identity.
/// </summary>
/// <remarks>
///     The <see cref="ISecretStore" /> is resolved lazily from <see cref="IServiceProvider" /> when the key
///     is first requested, so the provider does not assume a particular store registration order.
/// </remarks>
internal sealed class SecretStoreEncryptionKeyProvider(IServiceProvider services, string secretName)
    : IEncryptionKeyProvider
{
    private readonly IServiceProvider _services = services;

    private readonly string _secretName =
        string.IsNullOrWhiteSpace(secretName)
            ? throw new ArgumentException("Secret name must be provided.", nameof(secretName))
            : secretName;

    public async ValueTask<byte[]> GetKeyAsync(CancellationToken ct = default)
    {
        var secretStore = _services.GetService<ISecretStore>()
                          ?? throw new InvalidOperationException(
                              "No ISecretStore is registered. Register a secret store (e.g. Azure Key Vault) " +
                              "before resolving the encryption key from it.");

        var key = await secretStore.GetSecretAsync(_secretName, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException(
                $"Secret encryption key '{_secretName}' was not found in the configured secret store. " +
                "Store a base64-encoded 32-byte key under that name.");

        try
        {
            return Convert.FromBase64String(key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Secret encryption key '{_secretName}' from the secret store is not valid base64. " +
                "Store a base64-encoded 32-byte key.", ex);
        }
    }
}
