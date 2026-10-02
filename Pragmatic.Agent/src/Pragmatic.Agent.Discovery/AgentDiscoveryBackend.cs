using System.Text.Json;
using Pragmatic.Agent.Client;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Agent.Discovery;

/// <summary>
///     <see cref="IDiscoveryBackend"/> backed by the Pragmatic Agent KV. Each host's <b>full</b> topology
///     (modules + boundaries) is stored under <c>topology/{hostName}</c> and gossip-replicated across the
///     cluster, so cross-host DISC checks (module/db/provider conflicts) actually run in distributed mode.
/// </summary>
/// <remarks>
///     This is the distinct advantage over <c>SignalRDiscoveryBackend</c>, whose transport carried only
///     liveness (empty Modules/Boundaries). When the Agent daemon is unreachable the backend degrades
///     gracefully (store is a no-op, reads return empty) so the host still boots.
/// </remarks>
public sealed class AgentDiscoveryBackend(AgentConnection connection) : IDiscoveryBackend
{
    private const string Prefix = "topology/";

    /// <inheritdoc />
    public async Task StoreAsync(HostTopologyInfo topology, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (string.IsNullOrWhiteSpace(topology.HostName))
            throw new ArgumentException("topology.HostName must not be null or empty.", nameof(topology));

        if (!connection.IsConnected)
            return; // No daemon — skip; the host still starts (topology observability is auxiliary).

        var json = JsonSerializer.Serialize(topology);
        await connection.KvSetAsync($"{Prefix}{topology.HostName}", json, ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HostTopologyInfo?> GetByHostNameAsync(string hostName, CancellationToken ct = default)
    {
        if (!connection.IsConnected)
            return null;

        var (value, _, found) = await connection.KvGetAsync($"{Prefix}{hostName}", ct).ConfigureAwait(false);
        return found ? Deserialize(value) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HostTopologyInfo>> GetAllAsync(CancellationToken ct = default)
    {
        if (!connection.IsConnected)
            return [];

        var entries = await connection.KvPrefixAsync(Prefix, ct).ConfigureAwait(false);
        var result = new List<HostTopologyInfo>(entries.Count);
        foreach (var entry in entries)
        {
            var topology = Deserialize(entry.Value);
            if (topology is not null)
                result.Add(topology);
        }

        return result;
    }

    private static HostTopologyInfo? Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<HostTopologyInfo>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
