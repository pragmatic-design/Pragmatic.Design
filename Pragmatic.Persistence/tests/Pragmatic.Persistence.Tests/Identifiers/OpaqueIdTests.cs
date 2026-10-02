using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Identifiers;

namespace Pragmatic.Persistence.Tests.Identifiers;

public class OpaqueIdTests
{
    [Fact]
    public void Constructor_WithDefaultSalt_DoesNotThrow()
    {
        var act = () => new OpaqueId();

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithCustomSalt_DoesNotThrow()
    {
        var act = () => new OpaqueId("custom-salt");

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithNull_Throws()
    {
        var act = () => new OpaqueId(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEmpty_Throws()
    {
        var act = () => new OpaqueId("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithWhitespace_Throws()
    {
        var act = () => new OpaqueId("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_WithZero_ReturnsNonEmpty()
    {
        var opaque = new OpaqueId();

        var result = opaque.Encode(0);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Encode_WithPositiveNumber_ReturnsNonEmpty()
    {
        var opaque = new OpaqueId();

        var result = opaque.Encode(42);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Encode_WithLargeNumber_Works()
    {
        var opaque = new OpaqueId();

        var result = opaque.Encode(1_000_000_000);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Encode_WithNegative_ThrowsArgumentOutOfRange()
    {
        var opaque = new OpaqueId();

        var act = () => opaque.Encode(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Encode_ResultHasMinLength6()
    {
        var opaque = new OpaqueId();

        var result = opaque.Encode(0);

        result.Length.Should().BeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public void Encode_IsDeterministic()
    {
        var opaque = new OpaqueId("test-salt");

        var result1 = opaque.Encode(42);
        var result2 = opaque.Encode(42);

        result1.Should().Be(result2);
    }

    [Fact]
    public void Encode_DifferentSalts_DifferentResults()
    {
        var opaque1 = new OpaqueId("salt-one");
        var opaque2 = new OpaqueId("salt-two");

        var result1 = opaque1.Encode(42);
        var result2 = opaque2.Encode(42);

        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Encode_SequentialNumbers_LookDifferent()
    {
        var opaque = new OpaqueId();

        var results = new[] { opaque.Encode(1), opaque.Encode(2), opaque.Encode(3) };

        results.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void Decode_RoundTrip_ReturnsOriginal()
    {
        var opaque = new OpaqueId("round-trip-salt");

        for (long i = 0; i < 20; i++)
        {
            var encoded = opaque.Encode(i);
            var decoded = opaque.Decode(encoded);

            decoded.Should().Be(i, $"round-trip failed for number {i}");
        }
    }

    [Fact]
    public void Decode_WithNull_Throws()
    {
        var opaque = new OpaqueId();

        var act = () => opaque.Decode(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_WithInvalid_ThrowsArgumentException()
    {
        var opaque = new OpaqueId();

        var act = () => opaque.Decode("!@#$%^");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryDecode_WithValid_ReturnsTrueAndNumber()
    {
        var opaque = new OpaqueId();
        var encoded = opaque.Encode(99);

        var success = opaque.TryDecode(encoded, out var number);

        success.Should().BeTrue();
        number.Should().Be(99);
    }

    [Fact]
    public void TryDecode_WithNull_ReturnsFalse()
    {
        var opaque = new OpaqueId();

        opaque.TryDecode(null, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecode_WithEmpty_ReturnsFalse()
    {
        var opaque = new OpaqueId();

        opaque.TryDecode("", out _).Should().BeFalse();
    }

    [Fact]
    public void ToOpaque_FromOpaque_RoundTrip()
    {
        var encoded = OpaqueId.ToOpaque(123);
        var decoded = OpaqueId.FromOpaque(encoded);

        decoded.Should().Be(123);
    }

    [Fact]
    public void TryFromOpaque_WithValid_Works()
    {
        var encoded = OpaqueId.ToOpaque(456);

        var success = OpaqueId.TryFromOpaque(encoded, out var number);

        success.Should().BeTrue();
        number.Should().Be(456);
    }
}
