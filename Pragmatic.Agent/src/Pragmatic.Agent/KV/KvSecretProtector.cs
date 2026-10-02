using System.Security.Cryptography;
using System.Text;
using Pragmatic.Cryptography;

namespace Pragmatic.Agent.KV;

/// <summary>
///     Adapts <see cref="ISecretEncryptor" /> (AES-256-GCM, versioned and key-tagged) to the KV store's
///     string-in/string-out port, base64-encoding the packed ciphertext so it survives JSON persistence
///     and gossip replication.
/// </summary>
/// <remarks>
///     <para>
///         Replaces the agent's own AES implementation. Beyond removing a duplicate, this brings the KV
///         store key rotation: the ciphertext carries the id of the key that wrote it, so a previous key
///         can still decrypt older values while a new one encrypts.
///     </para>
///     <para>
///         <b>Transition reader.</b> Values written by the old implementation are framed
///         <c>nonce | ciphertext | tag</c>, whereas <see cref="AesGcmSecretEncryptor" /> expects
///         <c>nonce | tag | ciphertext</c> — the tag moved. The two are not interchangeable, so a value
///         that fails to decrypt under the current framing is retried under the old one before being
///         declared bad. Without this, retiring the old implementation would have made every secret
///         already in the KV undecryptable, and indistinguishable from a tampered one. Re-encryption is
///         lazy: the next write of a key stores it in the current format.
///     </para>
/// </remarks>
internal sealed class KvSecretProtector : IKvSecretProtector, IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly AesGcmSecretEncryptor _encryptor;

    /// <summary>
    ///     A private copy of the key material, kept for the transition reader: the encryptor zeroes the
    ///     ring's copy when disposed.
    /// </summary>
    private readonly byte[] _legacyKey;

    /// <summary>Creates a protector over a single AES-256 key.</summary>
    /// <param name="key">256-bit (32 bytes) encryption key.</param>
    public KvSecretProtector(byte[] key)
    {
        var encryptionKey = EncryptionKey.FromMaterial(key);
        _encryptor = new AesGcmSecretEncryptor(new EncryptionKeyRing(encryptionKey));
        _legacyKey = [.. key];
    }

    /// <summary>
    ///     Creates a protector from an environment variable or key file.
    ///     Checks: PRAGMATIC_AGENT_SECRET_KEY env → {dataDir}/secret.key file.
    ///     In a Production environment (ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT = Production),
    ///     throws <see cref="InvalidOperationException" /> when no key is available — silent
    ///     fallback would mask a misconfigured operator until a secret operation actually fails.
    ///     Outside production, returns <c>RejectEncryption</c> so secret reads/writes fail closed
    ///     while still letting the daemon start for non-secret workflows.
    /// </summary>
    public static IKvSecretProtector CreateFromEnvironment(string? dataDir = null)
    {
        // 1. Environment variable (base64-encoded 32 bytes).
        // A configured-but-invalid key is an operator misconfiguration: surface it loudly and abort
        // rather than silently falling through to "no key", which would hide the mistake until a
        // secret operation later fails at runtime.
        var envKey = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY");
        if (!string.IsNullOrEmpty(envKey))
        {
            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(envKey);
            }
            catch (FormatException ex)
            {
                const string msg = "PRAGMATIC_AGENT_SECRET_KEY is not valid base64.";
                AgentLogger.Error("Security", $"{msg} {ex.Message}");
                throw new InvalidOperationException(msg, ex);
            }

            if (keyBytes.Length != 32)
            {
                var msg = $"PRAGMATIC_AGENT_SECRET_KEY must decode to exactly 32 bytes (got {keyBytes.Length}).";
                AgentLogger.Error("Security", msg);
                throw new InvalidOperationException(msg);
            }

            return new KvSecretProtector(keyBytes);
        }

        // 2. Key file. As above: a present-but-corrupt key file is surfaced, not swallowed.
        if (dataDir is not null)
        {
            var keyFilePath = Path.Combine(dataDir, "secret.key");
            if (File.Exists(keyFilePath))
            {
                byte[] keyBytes;
                try
                {
                    keyBytes = Convert.FromBase64String(File.ReadAllText(keyFilePath).Trim());
                }
                catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
                {
                    var msg = $"secret.key at '{keyFilePath}' could not be read as base64.";
                    AgentLogger.Error("Security", $"{msg} {ex.Message}");
                    throw new InvalidOperationException(msg, ex);
                }

                if (keyBytes.Length != 32)
                {
                    var msg = $"secret.key at '{keyFilePath}' must decode to exactly 32 bytes (got {keyBytes.Length}).";
                    AgentLogger.Error("Security", msg);
                    throw new InvalidOperationException(msg);
                }

                return new KvSecretProtector(keyBytes);
            }
        }

        // 3. No key available.
        const string message = "No encryption key found: set PRAGMATIC_AGENT_SECRET_KEY (base64 32 bytes) " +
                               "or place a secret.key file under the data directory. Secret operations " +
                               "would otherwise be rejected at runtime.";

        if (IsProductionEnvironment())
        {
            AgentLogger.Error("Security", $"{message} Startup aborted in Production.");
            throw new InvalidOperationException(message);
        }

        AgentLogger.Error("Security", $"{message} Running with fail-closed secret handling (non-production).");
        return RejectEncryption.Instance;
    }

    private static bool IsProductionEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
               ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase)
            || string.Equals(env, "Prod", StringComparison.OrdinalIgnoreCase);
    }

    public string Encrypt(string plaintext) => Convert.ToBase64String(_encryptor.Encrypt(plaintext));

    public string Decrypt(string ciphertextBase64)
    {
        var packed = Convert.FromBase64String(ciphertextBase64);

        if (_encryptor.TryDecrypt(packed, out var plainText))
            return plainText;

        // Written by the pre-Pragmatic.Cryptography implementation: tag last, not after the nonce.
        if (TryDecryptLegacyFraming(packed, _legacyKey, out plainText))
            return plainText;

        throw new CryptographicException(
            "Unable to decrypt secret: authentication failed under the current key, in either the " +
            "current or the legacy framing (tampered value, or a key that no longer matches).");
    }

    /// <summary>Reads the retired <c>nonce | ciphertext | tag</c> framing.</summary>
    private static bool TryDecryptLegacyFraming(byte[] packed, byte[] key, out string plainText)
    {
        plainText = string.Empty;

        if (packed.Length < NonceSize + TagSize)
            return false;

        var nonce = packed.AsSpan(0, NonceSize);
        var tag = packed.AsSpan(packed.Length - TagSize, TagSize);
        var cipherText = packed.AsSpan(NonceSize, packed.Length - NonceSize - TagSize);
        var plainBytes = new byte[cipherText.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipherText, tag, plainBytes);
            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _encryptor.Dispose();
        CryptographicOperations.ZeroMemory(_legacyKey);
    }
}
