using Pragmatic.Agent.KV;

namespace Pragmatic.Agent.Gossip;

/// <summary>
///     Manages cluster lifecycle: initialization, discovery, joining peers.
///     Wraps <see cref="SwimProtocol"/> with higher-level cluster operations.
/// </summary>
internal sealed class ClusterManager(ClusterConfig config, KvStore kvStore) : IDisposable
{
    private readonly SwimProtocol _swim = new(
        config.AgentId,
        config.GossipPort,
        kvStore,
        config.SuspectTimeout,
        GossipAuthenticator.CreateFromConfig(config.SharedKey),
        config.BindAddress);

    /// <summary>The SWIM protocol instance (for direct access to membership).</summary>
    public SwimProtocol Protocol => _swim;

    /// <summary>Gets alive cluster members.</summary>
    public IReadOnlyList<ClusterMember> AliveMembers => _swim.Membership.GetAliveMembers();

    /// <summary>Gets all known members.</summary>
    public IReadOnlyList<ClusterMember> AllMembers => _swim.Membership.GetAllMembers();

    /// <summary>Total member count.</summary>
    public int MemberCount => _swim.Membership.Count;

    /// <summary>
    ///     Starts the cluster: begins gossip protocol and joins known peers.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        _swim.Start();

        // Hook KV changes to gossip replication. DropOldest keeps TryWrite from ever failing on a
        // full queue: a plain bounded (Wait) channel returned false under a write burst, and
        // KvStore.NotifyWatchers removes a subscription whose TryWrite fails — which permanently
        // stopped cross-node replication for the daemon's lifetime. DropOldest instead sheds the
        // oldest pending change (bounded memory, eventual-consistent) and keeps the subscription live.
        var channel = System.Threading.Channels.Channel.CreateBounded<KvChangeEvent>(
            new System.Threading.Channels.BoundedChannelOptions(1024)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        kvStore.Watch("", channel);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var change in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    _swim.EnqueueKvUpdate(change.Key, change.Value, change.Version, change.Deleted, change.Owner);
                }
            }
            catch (OperationCanceledException) { /* Normal shutdown */ }
            catch (Exception ex)
            {
                AgentLogger.Error("Cluster", $"KV replication loop failed: {ex.Message}");
            }
        }, ct);

        // Discover and join peers
        var peers = await DiscoverPeersAsync(ct).ConfigureAwait(false);

        foreach (var peer in peers)
        {
            try
            {
                var parts = peer.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                {
                    await _swim.JoinAsync(parts[0], port).ConfigureAwait(false);
                    AgentLogger.Info("Cluster", $"Joined peer: {peer}");
                }
            }
            catch (Exception ex)
            {
                AgentLogger.Warn("Cluster", $"Failed to join peer {peer}: {ex.Message}");
            }
        }

        if (peers.Count == 0)
            AgentLogger.Info("Cluster", "No peers found — running as single node");
        else
            AgentLogger.Info("Cluster", $"Contacted {peers.Count} peer(s)");
    }

    public void Dispose() => _swim.Dispose();

    private Task<IReadOnlyList<string>> DiscoverPeersAsync(CancellationToken ct)
    {
        return config.Discovery.ToLowerInvariant() switch
        {
            "static" => Task.FromResult<IReadOnlyList<string>>(config.Peers),
            "mdns" => DiscoverMdnsAsync(ct),
            "kubernetes" => DiscoverKubernetesAsync(ct),
            _ => Task.FromResult<IReadOnlyList<string>>([])
        };
    }

    /// <summary>
    ///     mDNS discovery: resolve _pragmatic._tcp.local via DNS.
    ///     Simplified implementation — full mDNS would use multicast.
    /// </summary>
    private static async Task<IReadOnlyList<string>> DiscoverMdnsAsync(CancellationToken ct)
    {
        // Simplified: try to resolve pragmatic-agent.local on common ports
        // Full mDNS implementation would use multicast UDP on 224.0.0.251:5353
        var results = new List<string>();

        try
        {
            var hostEntry = await System.Net.Dns.GetHostEntryAsync("pragmatic-agent.local", ct).ConfigureAwait(false);
            foreach (var addr in hostEntry.AddressList)
            {
                results.Add($"{addr}:9900");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // mDNS not available — single node mode.
            AgentLogger.Warn("Cluster", $"mDNS discovery failed: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    ///     Kubernetes discovery: resolve headless service DNS.
    ///     Expects env PRAGMATIC_AGENT_SERVICE for the headless service name.
    /// </summary>
    private static async Task<IReadOnlyList<string>> DiscoverKubernetesAsync(CancellationToken ct)
    {
        var serviceName = Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_SERVICE")
                          ?? "pragmatic-agent";

        var results = new List<string>();

        try
        {
            var hostEntry = await System.Net.Dns.GetHostEntryAsync(serviceName, ct).ConfigureAwait(false);
            foreach (var addr in hostEntry.AddressList)
            {
                results.Add($"{addr}:9900");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Headless service not reachable — single node mode.
            AgentLogger.Warn("Cluster", $"Kubernetes discovery failed for service '{serviceName}': {ex.Message}");
        }

        return results;
    }
}
