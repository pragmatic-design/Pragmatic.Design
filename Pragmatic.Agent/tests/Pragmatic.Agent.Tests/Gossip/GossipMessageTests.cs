using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Gossip;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     The SWIM gossip wire is UTF-8 JSON over UDP (see <c>SwimProtocol.SendToAsync</c> /
///     <c>ListenAsync</c>). These tests pin that contract: a <see cref="GossipMessage"/> — with its
///     piggy-backed membership (<see cref="MemberUpdate"/>) and KV (<see cref="KvUpdate"/>) updates —
///     must survive a System.Text.Json serialize → deserialize round-trip with no network involved.
/// </summary>
public class GossipMessageTests
{
    // Mirror the camelCase + null-omitting options SwimProtocol uses on the wire.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static GossipMessage RoundTrip(GossipMessage msg)
        => JsonSerializer.Deserialize<GossipMessage>(JsonSerializer.Serialize(msg, Options), Options)!;

    [Fact]
    public void Serialize_Deserialize_PingMessage_RoundTrips()
    {
        var decoded = RoundTrip(new GossipMessage { Type = GossipMessageType.Ping, SenderId = "node-a" });

        decoded.Type.Should().Be(GossipMessageType.Ping);
        decoded.SenderId.Should().Be("node-a");
    }

    [Fact]
    public void Serialize_Deserialize_PingReqAck_PreservesTypeAndTarget()
    {
        // PingReqAck is the indirect-probe success reply; type + targetId must survive so the
        // requester can resolve the pending indirect ack.
        var decoded = RoundTrip(new GossipMessage
        {
            Type = GossipMessageType.PingReqAck,
            SenderId = "helper",
            TargetId = "suspect-node"
        });

        decoded.Type.Should().Be(GossipMessageType.PingReqAck);
        decoded.TargetId.Should().Be("suspect-node");
    }

    [Fact]
    public void Serialize_Deserialize_WithMemberUpdates_RoundTrips()
    {
        var decoded = RoundTrip(new GossipMessage
        {
            Type = GossipMessageType.Sync,
            SenderId = "node-a",
            Members =
            [
                new MemberUpdate
                {
                    Id = "node-b", Host = "10.0.0.2", Port = 7946,
                    State = MemberState.Suspect, Incarnation = 5
                }
            ]
        });

        decoded.Members.Should().NotBeNull();
        var members = decoded.Members!;
        members.Should().HaveCount(1);
        var update = members[0];
        update.Id.Should().Be("node-b");
        update.Host.Should().Be("10.0.0.2");
        update.Port.Should().Be(7946);
        update.State.Should().Be(MemberState.Suspect);
        update.Incarnation.Should().Be(5);
    }

    [Fact]
    public void Serialize_Deserialize_WithKvUpdates_RoundTrips()
    {
        var ts = new DateTimeOffset(2026, 5, 30, 10, 0, 0, TimeSpan.Zero);
        var decoded = RoundTrip(new GossipMessage
        {
            Type = GossipMessageType.Sync,
            SenderId = "node-a",
            KvUpdates =
            [
                new KvUpdate { Key = "config/x", Value = "1", Version = 10, Deleted = false, UpdatedAt = ts },
                new KvUpdate { Key = "config/y", Value = null, Version = 11, Deleted = true, UpdatedAt = ts }
            ]
        });

        decoded.KvUpdates.Should().NotBeNull();
        var updates = decoded.KvUpdates!;
        updates.Should().HaveCount(2);
        updates[0].Key.Should().Be("config/x");
        updates[0].Value.Should().Be("1");
        updates[0].Version.Should().Be(10);
        updates[1].Deleted.Should().BeTrue();
        updates[1].Value.Should().BeNull();
    }

    [Fact]
    public void Serialize_Deserialize_NoPiggyback_LeavesCollectionsNull()
    {
        // With WhenWritingNull, absent piggyback arrays are omitted and deserialize back to null —
        // exactly how HandleMessageAsync guards them (`message.Members is not null`).
        var decoded = RoundTrip(new GossipMessage { Type = GossipMessageType.Ack, SenderId = "n" });

        decoded.Members.Should().BeNull();
        decoded.KvUpdates.Should().BeNull();
        decoded.TargetId.Should().BeNull();
    }

    [Fact]
    public void NewMessage_HasNullPiggybackAndDefaultType()
    {
        var msg = new GossipMessage { SenderId = "n" };

        msg.Members.Should().BeNull();
        msg.KvUpdates.Should().BeNull();
        msg.Type.Should().Be(default(GossipMessageType));
    }
}
