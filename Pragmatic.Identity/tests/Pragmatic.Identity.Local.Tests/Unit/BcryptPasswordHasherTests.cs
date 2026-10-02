using Pragmatic.Testing.Assertions;
using Pragmatic.Identity.Local.Services;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class BcryptPasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new(workFactor: 4); // Low factor for fast tests

    [Fact]
    public void Hash_ReturnsNonEmptyHash()
    {
        var hash = _hasher.Hash("password123");

        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe("password123");
    }

    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash("password123");

        _hasher.Verify("password123", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("password123");

        _hasher.Verify("wrong-password", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_ProducesDifferentHashesForSamePassword()
    {
        var hash1 = _hasher.Hash("password123");
        var hash2 = _hasher.Hash("password123");

        // BCrypt uses random salt, so hashes should differ
        hash1.Should().NotBe(hash2);

        // But both should verify correctly
        _hasher.Verify("password123", hash1).Should().BeTrue();
        _hasher.Verify("password123", hash2).Should().BeTrue();
    }

    [Fact]
    public void NeedsRehash_WhenStoredCostBelowConfigured_ReturnsTrue()
    {
        // Hash produced with cost 4 (the low-factor test hasher above).
        var weakHash = _hasher.Hash("password123");

        // A hasher configured for a higher work factor should want to upgrade it.
        var stronger = new BcryptPasswordHasher(workFactor: 6);

        stronger.NeedsRehash(weakHash).Should().BeTrue();
    }

    [Fact]
    public void NeedsRehash_WhenStoredCostMatchesConfigured_ReturnsFalse()
    {
        var hash = _hasher.Hash("password123");

        _hasher.NeedsRehash(hash).Should().BeFalse();
    }

    [Fact]
    public void NeedsRehash_WhenStoredCostAboveConfigured_ReturnsFalse()
    {
        var strongHash = new BcryptPasswordHasher(workFactor: 6).Hash("password123");

        // A hasher on a lower factor must not "downgrade" an already-stronger hash.
        _hasher.NeedsRehash(strongHash).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-bcrypt-hash")]
    [InlineData("$2a$")]
    public void NeedsRehash_WithMalformedHash_ReturnsFalse(string hash)
    {
        _hasher.NeedsRehash(hash).Should().BeFalse();
    }
}
