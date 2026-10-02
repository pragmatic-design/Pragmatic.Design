using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Identifiers;

namespace Pragmatic.Persistence.Tests.Identifiers;

public class ShortGuidTests
{
    [Fact]
    public void Encode_ReturnsExactly22Chars()
    {
        var guid = Guid.NewGuid();

        var result = ShortGuid.Encode(guid);

        result.Should().HaveLength(22);
    }

    [Fact]
    public void Encode_WithEmptyGuid_ReturnsDeterministicString()
    {
        var result1 = ShortGuid.Encode(Guid.Empty);
        var result2 = ShortGuid.Encode(Guid.Empty);

        result1.Should().Be(result2);
    }

    [Fact]
    public void Encode_ContainsOnlyUrlSafeChars()
    {
        // Generate many to increase coverage of alphabet
        var results = Enumerable.Range(0, 100)
            .Select(_ => ShortGuid.Encode(Guid.NewGuid()));

        foreach (var result in results)
        {
            result.Should().NotContain("+");
            result.Should().NotContain("/");
            result.Should().NotContain("=");
        }
    }

    [Fact]
    public void Decode_WithValidShortGuid_ReturnsOriginalGuid()
    {
        var original = Guid.NewGuid();
        var encoded = ShortGuid.Encode(original);

        var decoded = ShortGuid.Decode(encoded);

        decoded.Should().Be(original);
    }

    [Fact]
    public void Decode_WithNull_ThrowsArgumentException()
    {
        var act = () => ShortGuid.Decode(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_WithEmptyString_ThrowsArgumentException()
    {
        var act = () => ShortGuid.Decode("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_WithWrongLength_ThrowsArgumentException()
    {
        var act = () => ShortGuid.Decode("abcdefghijklmnopqrstu"); // 21 chars

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_WithInvalidBase64_ThrowsArgumentException()
    {
        var act = () => ShortGuid.Decode("!@#$%^&*()_+!@#$%^&*()"); // 22 invalid chars

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryDecode_WithValidInput_ReturnsTrueAndGuid()
    {
        var original = Guid.NewGuid();
        var encoded = ShortGuid.Encode(original);

        var success = ShortGuid.TryDecode(encoded, out var decoded);

        success.Should().BeTrue();
        decoded.Should().Be(original);
    }

    [Fact]
    public void TryDecode_WithNull_ReturnsFalse()
    {
        var result = ShortGuid.TryDecode(null, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryDecode_WithWrongLength_ReturnsFalse()
    {
        var result = ShortGuid.TryDecode("abcdefghijklmnopqrstuvw", out _); // 23 chars

        result.Should().BeFalse();
    }

    [Fact]
    public void TryDecode_WithNonCanonicalSlackBits_ReturnsFalse()
    {
        // 22 base64url chars carry 132 bits but a GUID is only 128: the final char has 4 slack
        // bits. A canonical encoding leaves those bits zero, so the canonical final symbol's
        // index is a multiple of 16 (low 4 bits = 0). A non-canonical input sets some slack bits:
        // it still decodes to the same 128-bit GUID, but is not what Encode would produce, so the
        // canonical-form guard must reject it instead of silently accepting the corruption.
        var canonical = ShortGuid.Encode(Guid.NewGuid());
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

        var canonicalIndex = alphabet.IndexOf(canonical[^1]);
        // Canonical final symbol carries only the top 2 real bits; slack (low 4) are zero.
        (canonicalIndex % 16).Should().Be(0, "a canonical ShortGuid leaves the 4 slack bits zero");

        // Set one slack bit -> same GUID bytes, different (non-canonical) string.
        var nonCanonical = canonical[..^1] + alphabet[canonicalIndex + 1];

        nonCanonical.Should().NotBe(canonical);
        nonCanonical.Should().HaveLength(ShortGuid.StringLength);

        ShortGuid.TryDecode(nonCanonical, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecode_RoundTripsForManyGuids()
    {
        foreach (var _ in Enumerable.Range(0, 200))
        {
            var original = Guid.NewGuid();
            var encoded = ShortGuid.Encode(original);

            ShortGuid.TryDecode(encoded, out var decoded).Should().BeTrue();
            decoded.Should().Be(original);
        }
    }

    [Fact]
    public void IsValid_WithValidShortGuid_ReturnsTrue()
    {
        var encoded = ShortGuid.Encode(Guid.NewGuid());

        ShortGuid.IsValid(encoded).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithNull_ReturnsFalse()
    {
        ShortGuid.IsValid(null).Should().BeFalse();
    }

    [Fact]
    public void StringLength_Is22()
    {
        ShortGuid.StringLength.Should().Be(22);
    }
}
