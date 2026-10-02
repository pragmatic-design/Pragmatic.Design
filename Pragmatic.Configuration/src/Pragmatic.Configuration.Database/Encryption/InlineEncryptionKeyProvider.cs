using Microsoft.Extensions.Options;
using Pragmatic.Cryptography;

namespace Pragmatic.Configuration.Database.Encryption;

/// <summary>
///     Default <see cref="IEncryptionKeyProvider" /> that decodes the inline base64 key from
///     <see cref="DatabaseConfigurationOptions.EncryptionKey" />, falling back to the
///     <c>PRAGMATIC_SECRET_KEY</c> environment variable. This preserves the original behavior so existing
///     hosts keep working unchanged.
/// </summary>
internal sealed class InlineEncryptionKeyProvider(IOptions<DatabaseConfigurationOptions> options)
    : IEncryptionKeyProvider
{
    private readonly DatabaseConfigurationOptions _options = options.Value;

    public ValueTask<byte[]> GetKeyAsync(CancellationToken ct = default)
    {
        var key = _options.EncryptionKey
                  ?? Environment.GetEnvironmentVariable("PRAGMATIC_SECRET_KEY");

        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException(
                "Secret encryption key not configured. Set DatabaseConfigurationOptions.EncryptionKey " +
                "or PRAGMATIC_SECRET_KEY environment variable (32-byte base64-encoded key).");

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Secret encryption key is not valid base64. " +
                "Provide a base64-encoded 32-byte key.", ex);
        }

        return ValueTask.FromResult(keyBytes);
    }
}
