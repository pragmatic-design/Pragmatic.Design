namespace Pragmatic.Cryptography;

/// <summary>
///     <see cref="IEncryptionKeyProvider" /> that reads the base64-encoded AES-256 key from a configurable
///     environment variable. Lets the host source the key from the process environment (CI/CD secret, K8s
///     secret mount, etc.) instead of embedding it in configuration.
/// </summary>
public sealed class EnvironmentEncryptionKeyProvider(string variableName) : IEncryptionKeyProvider
{
    private readonly string _variableName =
        string.IsNullOrWhiteSpace(variableName)
            ? throw new ArgumentException("Environment variable name must be provided.", nameof(variableName))
            : variableName;

    public ValueTask<byte[]> GetKeyAsync(CancellationToken ct = default)
    {
        var key = Environment.GetEnvironmentVariable(_variableName);

        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException(
                $"Secret encryption key not found in environment variable '{_variableName}'. " +
                "Set it to a base64-encoded 32-byte key.");

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Secret encryption key in environment variable '{_variableName}' is not valid base64. " +
                "Provide a base64-encoded 32-byte key.", ex);
        }

        return ValueTask.FromResult(keyBytes);
    }
}
