using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Configuration.Tests.Unit;

/// <summary>Verifies parsing of <see cref="SecretReference"/> (<c>secret://{key}</c>).</summary>
public class SecretReferenceTests
{
    [Theory]
    [InlineData("secret://db-password", "db-password")]
    [InlineData("secret://a/b/c", "a/b/c")]
    public void TryParse_ValidReference_ReturnsKey(string value, string expectedKey)
    {
        SecretReference.TryParse(value, out var key).Should().BeTrue();
        key.Should().Be(expectedKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain-value")]
    [InlineData("secret://")]          // scheme with empty key is not a valid reference
    [InlineData("Secret://Key")]       // scheme is case-sensitive
    public void TryParse_NonReference_ReturnsFalse(string? value)
    {
        SecretReference.TryParse(value, out var key).Should().BeFalse();
        key.Should().BeEmpty();
    }

    [Fact]
    public void Create_RoundTripsThroughTryParse()
    {
        var reference = SecretReference.Create("my-key");

        reference.Should().Be("secret://my-key");
        SecretReference.IsReference(reference).Should().BeTrue();
        SecretReference.TryParse(reference, out var key).Should().BeTrue();
        key.Should().Be("my-key");
    }
}
