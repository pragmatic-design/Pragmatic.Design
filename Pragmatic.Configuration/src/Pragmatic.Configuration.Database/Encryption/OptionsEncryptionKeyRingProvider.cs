using Microsoft.Extensions.Options;
using Pragmatic.Cryptography;

namespace Pragmatic.Configuration.Database.Encryption;

/// <summary>
///     Default <see cref="IEncryptionKeyRingProvider" />: the current key comes from the registered
///     <see cref="IEncryptionKeyProvider" /> (inline option / env var / secret store), and previous keys —
///     kept only to decrypt values written before a rotation — from
///     <see cref="DatabaseConfigurationOptions.PreviousEncryptionKeys" /> (base64-encoded 32-byte keys).
/// </summary>
/// <remarks>
///     Rotation is therefore configuration-only: set the new key as the current key and move the old one into
///     <c>PreviousEncryptionKeys</c>. New writes use the new key; existing secrets still decrypt under the old
///     one until a re-encrypt pass rewrites them.
/// </remarks>
internal sealed class OptionsEncryptionKeyRingProvider(
    IEncryptionKeyProvider currentKeyProvider,
    IOptions<DatabaseConfigurationOptions> options)
    : IEncryptionKeyRingProvider
{
    private readonly DatabaseConfigurationOptions _options = options.Value;

    public async ValueTask<EncryptionKeyRing> GetKeyRingAsync(CancellationToken ct = default)
    {
        var currentMaterial = await currentKeyProvider.GetKeyAsync(ct).ConfigureAwait(false);
        var current = EncryptionKey.FromMaterial(currentMaterial);

        var previous = new List<EncryptionKey>();
        if (_options.PreviousEncryptionKeys is { Count: > 0 } configured)
            foreach (var encoded in configured)
                previous.Add(EncryptionKey.FromMaterial(DecodeKey(encoded)));

        return new EncryptionKeyRing(current, previous);
    }

    private static byte[] DecodeKey(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "A previous encryption key is not valid base64. Provide base64-encoded 32-byte keys in " +
                "DatabaseConfigurationOptions.PreviousEncryptionKeys.", ex);
        }
    }
}
