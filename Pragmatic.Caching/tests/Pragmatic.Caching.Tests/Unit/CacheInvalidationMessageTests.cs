using Pragmatic.Caching.Redis;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>The message the nodes exchange survives the wire, and nothing else passes for one.</summary>
public class CacheInvalidationMessageTests
{
    [Theory]
    [InlineData(CacheInvalidationKind.Tag, "glossary")]
    [InlineData(CacheInvalidationKind.Key, "glossary:suggest|tenant=acme|term=cad")]
    public void AMessage_RoundTrips(CacheInvalidationKind kind, string value)
    {
        var sent = new CacheInvalidationMessage(kind, value, "node-a");

        CacheInvalidationMessage.FromWire(sent.ToWire()).Should().Be(sent,
            "the value goes last, so a key that contains the separator arrives whole");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X|node|value")]
    [InlineData("T|node")]
    [InlineData("T||value")]
    [InlineData("T|node|")]
    [InlineData("hello")]
    public void AnythingElse_IsNotAMessage(string? wire)
    {
        CacheInvalidationMessage.FromWire(wire).Should().BeNull(
            "another publisher on the channel must not be read as an invalidation");
    }
}
