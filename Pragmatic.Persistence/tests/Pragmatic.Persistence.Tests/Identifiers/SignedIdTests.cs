using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Identifiers;

namespace Pragmatic.Persistence.Tests.Identifiers;

public class SignedIdTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("a-32-byte-secret-signing-key!!!!");
    private static readonly byte[] OtherKey = Encoding.UTF8.GetBytes("a-different-32-byte-secret-key!!!");

    // =========================================================================
    // long round-trip
    // =========================================================================

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(1_000_000_000L)]
    [InlineData(long.MaxValue)]
    public void Encode_Decode_Long_RoundTrips(long id)
    {
        var token = SignedId.Encode(id, Key);

        SignedId.Decode(token, Key).Should().Be(id);
    }

    [Fact]
    public void TryDecode_Long_WithValidToken_ReturnsTrueAndId()
    {
        var token = SignedId.Encode(99L, Key);

        SignedId.TryDecode(token, Key, out long id).Should().BeTrue();
        id.Should().Be(99L);
    }

    [Fact]
    public void Encode_Long_Negative_ThrowsArgumentOutOfRange()
    {
        var act = () => SignedId.Encode(-1L, Key);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // =========================================================================
    // Guid round-trip
    // =========================================================================

    [Fact]
    public void Encode_Decode_Guid_RoundTrips()
    {
        var id = Guid.NewGuid();

        var token = SignedId.Encode(id, Key);

        SignedId.DecodeGuid(token, Key).Should().Be(id);
    }

    [Fact]
    public void Encode_Decode_Guid_Empty_RoundTrips()
    {
        var token = SignedId.Encode(Guid.Empty, Key);

        SignedId.DecodeGuid(token, Key).Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryDecode_Guid_WithValidToken_ReturnsTrueAndId()
    {
        var id = Guid.NewGuid();
        var token = SignedId.Encode(id, Key);

        SignedId.TryDecode(token, Key, out Guid decoded).Should().BeTrue();
        decoded.Should().Be(id);
    }

    // =========================================================================
    // Security: tampering / wrong key
    // =========================================================================

    [Fact]
    public void TryDecode_Long_WithWrongKey_ReturnsFalse()
    {
        var token = SignedId.Encode(123L, Key);

        SignedId.TryDecode(token, OtherKey, out long id).Should().BeFalse();
        id.Should().Be(0L);
    }

    [Fact]
    public void TryDecode_Guid_WithWrongKey_ReturnsFalse()
    {
        var token = SignedId.Encode(Guid.NewGuid(), Key);

        SignedId.TryDecode(token, OtherKey, out Guid id).Should().BeFalse();
        id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryDecode_AnySingleCharacterFlip_FailsVerification()
    {
        var token = SignedId.Encode(7777L, Key);

        // Flipping any one character must break the signature (or the base64 decode) — a forged
        // or altered token is NEVER decoded to a usable id.
        for (var i = 0; i < token.Length; i++)
        {
            var chars = token.ToCharArray();
            chars[i] = chars[i] == 'A' ? 'B' : 'A';
            var tampered = new string(chars);

            if (tampered == token)
                continue;

            SignedId.TryDecode(tampered, Key, out long id)
                .Should().BeFalse($"flipping char {i} must invalidate the token");
            id.Should().Be(0L);
        }
    }

    /// <summary>
    ///     Changes the first character of a token to something it is not.
    /// </summary>
    /// <remarks>
    ///     Not a hard-coded <c>"Z"</c>. The token comes from a random Guid, so roughly one time in
    ///     sixty-four it already starts with Z — the "tampered" token would then be identical to the
    ///     original, decoding would succeed, and the test would fail at random for having nothing to
    ///     detect.
    /// </remarks>
    private static string Tamper(string token)
        => (token[0] == 'Z' ? 'Y' : 'Z') + token[1..];

    [Fact]
    public void Decode_Long_Tampered_Throws()
    {
        var token = SignedId.Encode(5L, Key);
        var tampered = Tamper(token);

        var act = () => SignedId.Decode(tampered, Key);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DecodeGuid_Tampered_Throws()
    {
        var token = SignedId.Encode(Guid.NewGuid(), Key);
        var tampered = Tamper(token);

        var act = () => SignedId.DecodeGuid(tampered, Key);

        act.Should().Throw<ArgumentException>();
    }

    // =========================================================================
    // Malformed / length / null input
    // =========================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!not-base64!!!")]
    [InlineData("short")]
    public void TryDecode_Long_MalformedToken_ReturnsFalse(string? token)
    {
        SignedId.TryDecode(token, Key, out long id).Should().BeFalse();
        id.Should().Be(0L);
    }

    [Fact]
    public void TryDecode_LongTokenAsGuid_RejectedByLength()
    {
        // A long token is shorter than a Guid token: decoding the wrong type must fail on the
        // fixed canonical length check, never silently misinterpret payload bytes.
        var longToken = SignedId.Encode(123L, Key);

        SignedId.TryDecode(longToken, Key, out Guid id).Should().BeFalse();
        id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryDecode_GuidTokenAsLong_RejectedByLength()
    {
        var guidToken = SignedId.Encode(Guid.NewGuid(), Key);

        SignedId.TryDecode(guidToken, Key, out long id).Should().BeFalse();
        id.Should().Be(0L);
    }

    // =========================================================================
    // Key validation
    // =========================================================================

    [Fact]
    public void Encode_Long_EmptyKey_Throws()
    {
        var act = () => SignedId.Encode(1L, ReadOnlySpan<byte>.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_Guid_EmptyKey_Throws()
    {
        var act = () => SignedId.Encode(Guid.NewGuid(), ReadOnlySpan<byte>.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryDecode_EmptyKey_Throws()
    {
        var token = SignedId.Encode(1L, Key);

        var act = () => SignedId.TryDecode(token, ReadOnlySpan<byte>.Empty, out long _);

        act.Should().Throw<ArgumentException>();
    }

    // =========================================================================
    // Token shape
    // =========================================================================

    [Fact]
    public void Encode_ProducesUrlSafeToken()
    {
        var token = SignedId.Encode(123456789L, Key);

        token.Should().NotContain("+");
        token.Should().NotContain("/");
        token.Should().NotContain("=");
    }

    [Fact]
    public void Encode_DifferentKeys_ProduceDifferentTokens()
    {
        var a = SignedId.Encode(42L, Key);
        var b = SignedId.Encode(42L, OtherKey);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Encode_IsDeterministic_ForSameKeyAndId()
    {
        SignedId.Encode(42L, Key).Should().Be(SignedId.Encode(42L, Key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    public void Encode_KeyShorterThanMinimum_Throws(int keyLength)
    {
        // A tiny key makes the HMAC brute-forceable, defeating tamper-evidence. Reject it.
        var weakKey = new byte[keyLength];

        var act = () => SignedId.Encode(42L, weakKey);

        act.Should().Throw<ArgumentException>().WithMessage("*at least 16 bytes*");
    }

    [Fact]
    public void Encode_MinimumLengthKey_Succeeds()
    {
        var key = new byte[SignedId.MinimumKeyLength];

        var token = SignedId.Encode(42L, key);

        SignedId.Decode(token, key).Should().Be(42L);
    }
}
