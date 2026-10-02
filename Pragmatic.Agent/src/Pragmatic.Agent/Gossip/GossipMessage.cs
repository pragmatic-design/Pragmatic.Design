using System.Text.Json.Serialization;

namespace Pragmatic.Agent.Gossip;

/// <summary>Wire message for agent-to-agent gossip communication.</summary>
internal sealed class GossipMessage
{
    [JsonPropertyName("type")]
    public GossipMessageType Type { get; init; }

    [JsonPropertyName("senderId")]
    public required string SenderId { get; init; }

    /// <summary>Membership updates piggybacked on every message.</summary>
    [JsonPropertyName("members")]
    public MemberUpdate[]? Members { get; init; }

    /// <summary>KV mutations piggybacked on every message.</summary>
    [JsonPropertyName("kvUpdates")]
    public KvUpdate[]? KvUpdates { get; init; }

    /// <summary>Target member ID (for Ping/PingReq).</summary>
    [JsonPropertyName("targetId")]
    public string? TargetId { get; init; }
}

internal enum GossipMessageType
{
    Ping = 1,
    Ack = 2,
    PingReq = 3,       // Indirect probe: "ping targetId for me"
    PingReqAck = 4,    // Response to PingReq
    Join = 5,          // New member announcing itself
    Sync = 6,          // Full state: on join, and each anti-entropy round
    Pull = 7           // "Send me your state": the pull half of an anti-entropy round
}

/// <summary>Membership state change piggybacked on gossip messages.</summary>
internal sealed class MemberUpdate
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("host")]
    public required string Host { get; init; }

    [JsonPropertyName("port")]
    public required int Port { get; init; }

    [JsonPropertyName("state")]
    public required MemberState State { get; init; }

    [JsonPropertyName("incarnation")]
    public required long Incarnation { get; init; }
}

/// <summary>KV mutation piggybacked on gossip messages (LWW replication).</summary>
internal sealed class KvUpdate
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("version")]
    public required long Version { get; init; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; init; }

    [JsonPropertyName("updatedAt")]
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The Agent that owns an ephemeral entry (<c>KvEntry.Owner</c>); null for a plain one.</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; init; }
}
