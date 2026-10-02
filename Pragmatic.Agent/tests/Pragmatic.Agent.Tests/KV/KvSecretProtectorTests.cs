// CS0618: the AES-GCM API surface used transitively by KvSecretProtector is flagged obsolete on
// this TFM; the encryptor is the unit under test and is not ours to change, so the diagnostic is
// suppressed for this test file only.
#pragma warning disable CS0618

using System.Security.Cryptography;
using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.KV;
using Xunit;

namespace Pragmatic.Agent.Tests.KV;

/// <summary>
///     Covers the KV secret protector: round-trip integrity, key validation, ciphertext
///     framing/tamper detection, reading values written in the retired framing, and the
///     <see cref="KvSecretProtector.CreateFromEnvironment"/> factory (env-var key, invalid key,
///     missing key fail-closed). Environment mutations are restored in a <c>finally</c> so the tests
///     stay isolated and order-independent.
/// </summary>
[Collection(EnvironmentSensitiveCollection.Name)]
public class KvSecretProtectorTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Ctor_WrongKeyLength_Throws()
    {
        var act = () => new KvSecretProtector(new byte[16]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_RecoversPlaintext()
    {
        var enc = new KvSecretProtector(NewKey());

        var cipher = enc.Encrypt("super-secret-value");
        var plain = enc.Decrypt(cipher);

        plain.Should().Be("super-secret-value");
    }

    [Fact]
    public void EncryptDecrypt_EmptyString_RoundTrips()
    {
        var enc = new KvSecretProtector(NewKey());

        enc.Decrypt(enc.Encrypt(string.Empty)).Should().Be(string.Empty);
    }

    [Fact]
    public void EncryptDecrypt_UnicodePayload_RoundTrips()
    {
        var enc = new KvSecretProtector(NewKey());
        const string value = "città — 日本語 — 😀";

        enc.Decrypt(enc.Encrypt(value)).Should().Be(value);
    }

    [Fact]
    public void Encrypt_SamePlaintext_ProducesDifferentCiphertext()
    {
        // A random nonce per call means ciphertexts must differ even for identical input.
        var enc = new KvSecretProtector(NewKey());

        enc.Encrypt("same").Should().NotBe(enc.Encrypt("same"));
    }

    [Fact]
    public void Encrypt_OutputIsValidBase64()
    {
        var enc = new KvSecretProtector(NewKey());

        var act = () => Convert.FromBase64String(enc.Encrypt("x"));
        act.Should().NotThrow();
    }

    [Fact]
    public void Decrypt_WithDifferentKey_Throws()
    {
        var cipher = new KvSecretProtector(NewKey()).Encrypt("value");

        var act = () => new KvSecretProtector(NewKey()).Decrypt(cipher);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var key = NewKey();
        var enc = new KvSecretProtector(key);
        var packed = Convert.FromBase64String(enc.Encrypt("value"));

        // Flip a bit inside the ciphertext body; GCM tag verification must reject it.
        packed[^1] ^= 0xFF;

        var act = () => enc.Decrypt(Convert.ToBase64String(packed));
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_TooShortCiphertext_Throws()
    {
        var enc = new KvSecretProtector(NewKey());

        // Fewer bytes than nonce(12) + tag(16).
        var act = () => enc.Decrypt(Convert.ToBase64String(new byte[10]));
        act.Should().Throw<CryptographicException>();
    }

    // =========================================================================
    // Transition from the retired framing
    //
    // The retired framing is nonce | ciphertext | tag, while Pragmatic.Cryptography packs
    // nonce | tag | ciphertext. Without reading the retired framing, every secret already written
    // in it would be undecryptable — and, because a mis-framed GCM decrypt fails its tag check,
    // indistinguishable from a tampered value.
    // =========================================================================

    [Fact]
    public void Decrypt_ValueWrittenInRetiredFraming_StillReadable()
    {
        var key = NewKey();
        var legacyCipher = EncryptWithRetiredFraming(key, "written-before-the-migration");

        var plain = new KvSecretProtector(key).Decrypt(legacyCipher);

        plain.Should().Be("written-before-the-migration");
    }

    [Fact]
    public void Decrypt_RetiredFramingWithWrongKey_StillThrows()
    {
        // The transition reader must not weaken authentication: a value it cannot authenticate
        // is still rejected.
        var legacyCipher = EncryptWithRetiredFraming(NewKey(), "value");

        var act = () => new KvSecretProtector(NewKey()).Decrypt(legacyCipher);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Encrypt_WritesCurrentVersionedFraming_NotTheRetiredOne()
    {
        // Re-encryption is lazy: reads accept the old framing, but every write must produce the
        // current one, so the old framing drains away instead of being perpetuated.
        var packed = Convert.FromBase64String(new KvSecretProtector(NewKey()).Encrypt("x"));

        packed[0].Should().Be(0x01, "the versioned format starts with its version byte");
    }

    /// <summary>Packs a value the way the retired agent encryptor did: nonce, ciphertext, then tag.</summary>
    private static string EncryptWithRetiredFraming(byte[] key, string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipherText = new byte[plainBytes.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, plainBytes, cipherText, tag);

        var packed = new byte[nonce.Length + cipherText.Length + tag.Length];
        nonce.CopyTo(packed, 0);
        cipherText.CopyTo(packed, nonce.Length);
        tag.CopyTo(packed, nonce.Length + cipherText.Length);

        return Convert.ToBase64String(packed);
    }

    [Fact]
    public void CreateFromEnvironment_ValidEnvKey_ReturnsWorkingEncryptor()
    {
        var key = Convert.ToBase64String(NewKey());
        var original = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", key);

            var enc = KvSecretProtector.CreateFromEnvironment();

            enc.Should().BeOfType<KvSecretProtector>();
            enc.Decrypt(enc.Encrypt("hello")).Should().Be("hello");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", original);
        }
    }

    [Fact]
    public void CreateFromEnvironment_NonBase64EnvKey_ThrowsInvalidOperation()
    {
        var original = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", "not!base64!!!");

            var act = () => KvSecretProtector.CreateFromEnvironment();
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", original);
        }
    }

    [Fact]
    public void CreateFromEnvironment_WrongLengthEnvKey_ThrowsInvalidOperation()
    {
        var original = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY");
        try
        {
            // Valid base64 but decodes to 16 bytes, not the required 32.
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", Convert.ToBase64String(new byte[16]));

            var act = () => KvSecretProtector.CreateFromEnvironment();
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", original);
        }
    }

    [Fact]
    public void CreateFromEnvironment_KeyFile_ReturnsWorkingEncryptor()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agent-secret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var envKey = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", null);
            File.WriteAllText(Path.Combine(dir, "secret.key"), Convert.ToBase64String(NewKey()));

            var enc = KvSecretProtector.CreateFromEnvironment(dir);

            enc.Should().BeOfType<KvSecretProtector>();
            enc.Decrypt(enc.Encrypt("from-file")).Should().Be("from-file");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", envKey);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CreateFromEnvironment_CorruptKeyFile_ThrowsInvalidOperation()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"agent-secret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var envKey = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", null);
            File.WriteAllText(Path.Combine(dir, "secret.key"), "@@@not-base64@@@");

            var act = () => KvSecretProtector.CreateFromEnvironment(dir);
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", envKey);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CreateFromEnvironment_NoKeyNonProduction_ReturnsFailClosed()
    {
        var envKey = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY");
        var aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);

            var enc = KvSecretProtector.CreateFromEnvironment();

            // Fail-closed: the encryptor exists but every secret operation must throw.
            enc.Should().BeOfType<RejectEncryption>();
            var act = () => enc.Encrypt("x");
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", envKey);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspnet);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", dotnet);
        }
    }

    [Fact]
    public void CreateFromEnvironment_NoKeyProduction_ThrowsInvalidOperation()
    {
        var envKey = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY");
        var aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);

            var act = () => KvSecretProtector.CreateFromEnvironment();
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_SECRET_KEY", envKey);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", aspnet);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", dotnet);
        }
    }
}

/// <summary>
///     Covers the two pass-through / fail-closed <see cref="IKvSecretProtector"/> implementations.
/// </summary>
public class NoEncryptionTests
{
    [Fact]
    public void Encrypt_ReturnsPlaintextUnchanged()
    {
        NoEncryption.Instance.Encrypt("value").Should().Be("value");
    }

    [Fact]
    public void Decrypt_ReturnsCiphertextUnchanged()
    {
        NoEncryption.Instance.Decrypt("value").Should().Be("value");
    }
}

public class RejectEncryptionTests
{
    [Fact]
    public void Encrypt_AlwaysThrows()
    {
        var act = () => RejectEncryption.Instance.Encrypt("value");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Decrypt_AlwaysThrows()
    {
        var act = () => RejectEncryption.Instance.Decrypt("value");
        act.Should().Throw<InvalidOperationException>();
    }
}
