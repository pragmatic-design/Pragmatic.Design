namespace Pragmatic.Agent.Gossip;

/// <summary>
///     Configuration for the gossip cluster.
/// </summary>
internal sealed class ClusterConfig
{
    /// <summary>Unique agent ID (auto-generated if not set).</summary>
    public string AgentId { get; set; } = $"agent-{Environment.MachineName}-{Random.Shared.Next(1000, 9999)}";

    /// <summary>UDP port for gossip communication. Default: 9900.</summary>
    public int GossipPort { get; set; } = 9900;

    /// <summary>
    ///     IP address the gossip UDP socket binds to. Default: <c>0.0.0.0</c> (all interfaces) so
    ///     agents on different hosts can form a cluster. Set to <c>127.0.0.1</c> for a single-host
    ///     deployment, or to a specific private NIC address to restrict gossip to a trusted network.
    ///     Regardless of bind address, every datagram is HMAC-authenticated (see <see cref="SharedKey"/>).
    /// </summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>
    ///     Cluster-shared key used to HMAC-authenticate every gossip datagram. All agents in a cluster
    ///     MUST share the same value. May be base64 (>= 16 decoded bytes) or any raw string. When null,
    ///     resolution falls back to the <c>PRAGMATIC_AGENT_GOSSIP_KEY</c> environment variable. When no
    ///     key is configured at all, gossip runs FAIL-CLOSED: inbound KV/membership mutations from
    ///     remote peers are rejected (see <see cref="SwimProtocol"/>).
    /// </summary>
    public string? SharedKey { get; set; }

    /// <summary>Discovery mode: "static", "mdns", "kubernetes", "aca".</summary>
    public string Discovery { get; set; } = "static";

    /// <summary>Static peer addresses (host:port). Used when Discovery = "static".</summary>
    public List<string> Peers { get; set; } = [];

    /// <summary>Time before a suspect member is declared dead. Default: 5s.</summary>
    public TimeSpan SuspectTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
