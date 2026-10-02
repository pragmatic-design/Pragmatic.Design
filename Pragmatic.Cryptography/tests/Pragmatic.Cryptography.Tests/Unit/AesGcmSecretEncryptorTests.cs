using System.Security.Cryptography;
using System.Text;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Cryptography.Tests.Unit;

public sealed class AesGcmSecretEncryptorTests : IDisposable
{
    private readonly AesGcmSecretEncryptor _encryptor;

    public AesGcmSecretEncryptorTests()
        => _encryptor = new AesGcmSecretEncryptor(RingOf(FilledKey(0xAB)));

    private static byte[] FilledKey(byte fill)
    {
        var key = new byte[32];
        Array.Fill(key, fill);
        return key;
    }

    private static EncryptionKeyRing RingOf(byte[] material, params byte[][] previous)
        => new(EncryptionKey.FromMaterial(material),
            previous.Select(EncryptionKey.FromMaterial).ToList());

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        var plainText = "my-super-secret-api-key";
        var encrypted = _encryptor.Encrypt(plainText);
        var decrypted = _encryptor.Decrypt(encrypted);

        decrypted.Should().Be(plainText);
    }

    [Fact]
    public void Encrypt_ProducesDifferentOutputEachTime()
    {
        var a = _encryptor.Encrypt("same-input");
        var b = _encryptor.Encrypt("same-input");

        a.Should().NotBeEquivalentTo(b);
    }

    [Fact]
    public void Encrypt_OutputIsVersionedAndKeyTagged()
    {
        var encrypted = _encryptor.Encrypt("test");

        encrypted[0].Should().Be(0x01, "new writes use the versioned format");
        encrypted[1].Should().Be((byte)EncryptionKey.KeyIdLength, "the key id length prefix");
        // version(1) + keyIdLen(1) + keyId(8) + nonce(12) + tag(16) + ciphertext(>=4)
        encrypted.Length.Should().BeGreaterThanOrEqualTo(2 + 8 + 12 + 16 + 4);
    }

    [Fact]
    public void Decrypt_TamperedData_Throws()
    {
        var encrypted = _encryptor.Encrypt("secret");
        encrypted[^1] ^= 0xFF;

        var act = () => _encryptor.Decrypt(encrypted);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void FromMaterial_InvalidKeyLength_Throws()
    {
        var act = () => EncryptionKey.FromMaterial(new byte[16]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EncryptDecrypt_EmptyString_Works()
    {
        var encrypted = _encryptor.Encrypt("");
        _encryptor.Decrypt(encrypted).Should().BeEmpty();
    }

    [Fact]
    public void EncryptDecrypt_UnicodeString_Works()
    {
        var plainText = "パスワード_密码_пароль";
        var encrypted = _encryptor.Encrypt(plainText);
        _encryptor.Decrypt(encrypted).Should().Be(plainText);
    }

    [Fact]
    public void EncryptDecrypt_WithAssociatedData_RoundTrips()
    {
        var encrypted = _encryptor.Encrypt("secret", "tenant-1:api-key");
        _encryptor.Decrypt(encrypted, "tenant-1:api-key").Should().Be("secret");
    }

    [Fact]
    public void Decrypt_WrongAssociatedData_Fails()
    {
        var encrypted = _encryptor.Encrypt("secret", "tenant-1:api-key");
        _encryptor.TryDecrypt(encrypted, out _, "tenant-2:api-key").Should().BeFalse();
    }

    [Fact]
    public void Rotation_PreviousKeyValue_DecryptsUnderNewRing()
    {
        // Value written under the old key A...
        var oldKey = FilledKey(0xA1);
        using var oldEncryptor = new AesGcmSecretEncryptor(RingOf(oldKey));
        var encrypted = oldEncryptor.Encrypt("rotate-me", "aad");

        // ...still decrypts after rotating to B, because A is kept as a previous key.
        var newKey = FilledKey(0xB2);
        using var rotated = new AesGcmSecretEncryptor(RingOf(newKey, oldKey));

        rotated.Decrypt(encrypted, "aad").Should().Be("rotate-me");
    }

    [Fact]
    public void Rotation_NewKeyValue_NotDecryptableByOldRingAlone()
    {
        var newKey = FilledKey(0xB2);
        using var rotated = new AesGcmSecretEncryptor(RingOf(newKey, FilledKey(0xA1)));
        var encrypted = rotated.Encrypt("only-new", "aad");

        using var oldOnly = new AesGcmSecretEncryptor(RingOf(FilledKey(0xA1)));
        oldOnly.TryDecrypt(encrypted, out _, "aad").Should().BeFalse(
            "a value written with the new key cannot be read by a ring that lacks it");
    }

    [Fact]
    public void Legacy_UnversionedCiphertext_StillDecrypts()
    {
        // Reproduce the pre-rotation on-disk format: [12B nonce][16B tag][ciphertext], no version prefix.
        var key = FilledKey(0xCD);
        var legacy = EncryptLegacy(key, "legacy-secret", "aad");

        using var encryptor = new AesGcmSecretEncryptor(RingOf(key));
        encryptor.Decrypt(legacy, "aad").Should().Be("legacy-secret");
    }

    private static byte[] EncryptLegacy(byte[] key, string plainText, string aad)
    {
        using var aes = new AesGcm(key, 16);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(aad));

        var packed = new byte[12 + 16 + cipher.Length];
        nonce.CopyTo(packed, 0);
        tag.CopyTo(packed, 12);
        cipher.CopyTo(packed, 28);
        return packed;
    }

    public void Dispose() => _encryptor.Dispose();
}
