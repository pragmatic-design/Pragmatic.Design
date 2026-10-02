using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class SecretEntryTests
{
    [Fact]
    public void Found_WithValue_IsTrue()
    {
        var entry = new SecretEntry("s3cret");

        entry.Found.Should().BeTrue();
        entry.Value.Should().Be("s3cret");
    }

    [Fact]
    public void Found_WithNullValue_IsFalse()
    {
        var entry = new SecretEntry((string?)null);

        entry.Found.Should().BeFalse();
    }

    [Fact]
    public void NotFound_HasNullValueAndNoMetadata()
    {
        SecretEntry.NotFound.Found.Should().BeFalse();
        SecretEntry.NotFound.Value.Should().BeNull();
        SecretEntry.NotFound.ExpiresAt.Should().BeNull();
        SecretEntry.NotFound.RotatedAt.Should().BeNull();
    }

    [Fact]
    public void IsExpired_WithNoExpiry_IsFalse()
    {
        var entry = new SecretEntry("v");

        entry.IsExpired(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_BeforeExpiry_IsFalse()
    {
        var expires = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var entry = new SecretEntry("v", ExpiresAt: expires);

        entry.IsExpired(expires.AddSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_AtExpiry_IsTrue_Inclusive()
    {
        var expires = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var entry = new SecretEntry("v", ExpiresAt: expires);

        entry.IsExpired(expires).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_AfterExpiry_IsTrue()
    {
        var expires = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var entry = new SecretEntry("v", ExpiresAt: expires);

        entry.IsExpired(expires.AddSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void RotatedAt_IsCarried()
    {
        var rotated = new DateTimeOffset(2029, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var entry = new SecretEntry("v", RotatedAt: rotated);

        entry.RotatedAt.Should().Be(rotated);
    }
}
