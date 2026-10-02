using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Tests.Unit;

/// <summary>
///     <see cref="ExternalIdentityKey" /> — the one definition of the format, so that every place that
///     composes a key composes it the same way.
/// </summary>
public class ExternalIdentityKeyTests
{
    [Fact]
    public void Compose_JoinsIssuerAndSubject()
        => ExternalIdentityKey.Compose("local", "alice").Should().Be("local|alice");

    /// <summary>
    ///     The reason the format escapes at all: without it these two principals share one key, and
    ///     one of them resolves to the other's identity record.
    /// </summary>
    [Fact]
    public void Compose_ForTwoPrincipalsDifferingOnlyBySeparatorPlacement_ProducesDifferentKeys()
        => ExternalIdentityKey.Compose("a|b", "c")
            .Should().NotBe(ExternalIdentityKey.Compose("a", "b|c"));

    [Fact]
    public void Compose_EscapesTheSeparatorInEitherHalf()
    {
        ExternalIdentityKey.Compose("a|b", "c").Should().Be("a%7Cb|c");
        ExternalIdentityKey.Compose("a", "b|c").Should().Be("a|b%7Cc");
    }

    /// <summary>The composed key always has exactly one separator, whatever the halves contain.</summary>
    [Fact]
    public void Compose_LeavesExactlyOneSeparator()
        => ExternalIdentityKey.Compose("iss|with|pipes", "sub|with|pipes")!
            .Count(c => c == ExternalIdentityKey.Separator).Should().Be(1);

    /// <summary>Half a key correlates nothing, so it is not a key.</summary>
    [Theory]
    [InlineData(null, "subject")]
    [InlineData("issuer", null)]
    [InlineData("", "subject")]
    [InlineData("issuer", "")]
    [InlineData(null, null)]
    public void Compose_WithEitherHalfMissing_ReturnsNull(string? issuer, string? subject)
        => ExternalIdentityKey.Compose(issuer, subject).Should().BeNull();
}
