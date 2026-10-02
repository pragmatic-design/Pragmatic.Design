using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Gossip;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     The gossip UDP socket is unauthenticated at the network layer, so every datagram carries a
///     cluster-shared HMAC-SHA256 (<see cref="GossipAuthenticator"/>). These tests pin the trust
///     boundary: a correctly-signed datagram verifies and yields its payload; an unsigned, tampered,
///     truncated, or wrong-key datagram is DROPPED.
/// </summary>
public class GossipAuthenticatorTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("cluster-shared-secret-key-0123456789");

    [Fact]
    public void Sign_ThenVerify_WithSameKey_AcceptsAndReturnsPayload()
    {
        var auth = new GossipAuthenticator(Key);
        var payload = Encoding.UTF8.GetBytes("""{"type":1,"senderId":"a"}""");

        var datagram = auth.Sign(payload);

        auth.TryVerify(datagram, out var recovered).Should().BeTrue();
        recovered.ToArray().Should().Equal(payload);
    }

    [Fact]
    public void TryVerify_TamperedPayload_IsRejected()
    {
        var auth = new GossipAuthenticator(Key);
        var datagram = auth.Sign(Encoding.UTF8.GetBytes("original-payload"));

        // Flip a byte in the payload region (after the 32-byte tag).
        datagram[GossipAuthenticator.TagSize] ^= 0xFF;

        auth.TryVerify(datagram, out _).Should().BeFalse();
    }

    [Fact]
    public void TryVerify_TamperedTag_IsRejected()
    {
        var auth = new GossipAuthenticator(Key);
        var datagram = auth.Sign(Encoding.UTF8.GetBytes("payload"));

        datagram[0] ^= 0xFF; // Corrupt the MAC tag itself.

        auth.TryVerify(datagram, out _).Should().BeFalse();
    }

    [Fact]
    public void TryVerify_UnsignedPayload_IsRejected()
    {
        var auth = new GossipAuthenticator(Key);

        // A raw payload with no prepended tag (what an attacker who doesn't know the key can send).
        var unsigned = Encoding.UTF8.GetBytes("""{"type":1,"senderId":"attacker","kvUpdates":[]}""");

        auth.TryVerify(unsigned, out _).Should().BeFalse();
    }

    [Fact]
    public void TryVerify_WrongKey_IsRejected()
    {
        var signer = new GossipAuthenticator(Key);
        var datagram = signer.Sign(Encoding.UTF8.GetBytes("payload"));

        var verifier = new GossipAuthenticator(Encoding.UTF8.GetBytes("a-totally-different-cluster-key!!"));

        verifier.TryVerify(datagram, out _).Should().BeFalse();
    }

    [Fact]
    public void TryVerify_TooShortToContainTag_IsRejected()
    {
        var auth = new GossipAuthenticator(Key);

        auth.TryVerify(new byte[GossipAuthenticator.TagSize - 1], out _).Should().BeFalse();
    }

    [Fact]
    public void CreateFromConfig_NoKeyAnywhere_ReturnsNull()
    {
        // No explicit key and (in CI) no env var → fail-closed signal to the caller.
        Environment.SetEnvironmentVariable("PRAGMATIC_AGENT_GOSSIP_KEY", null);

        GossipAuthenticator.CreateFromConfig(null).Should().BeNull();
    }

    [Fact]
    public void CreateFromConfig_ExplicitKey_BuildsAuthenticatorThatRoundTrips()
    {
        var auth = GossipAuthenticator.CreateFromConfig("an-explicit-cluster-key-value-123");
        auth.Should().NotBeNull();

        var datagram = auth!.Sign(Encoding.UTF8.GetBytes("x"));
        auth.TryVerify(datagram, out _).Should().BeTrue();
    }

    [Fact]
    public void CreateFromConfig_TwoNodesSameKey_InteroperateAcrossInstances()
    {
        // Cross-node interop: a datagram signed on node A verifies on node B configured with the
        // same key string (the realistic cluster setup).
        const string shared = "Y2x1c3Rlci1zaGFyZWQta2V5LWJhc2U2NA==";
        var nodeA = GossipAuthenticator.CreateFromConfig(shared)!;
        var nodeB = GossipAuthenticator.CreateFromConfig(shared)!;

        var datagram = nodeA.Sign(Encoding.UTF8.GetBytes("hello-cluster"));
        nodeB.TryVerify(datagram, out var payload).Should().BeTrue();
        Encoding.UTF8.GetString(payload).Should().Be("hello-cluster");
    }
}
