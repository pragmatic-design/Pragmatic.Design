// Pragmatic.Discovery - InMemory Backend

using System.Collections.Concurrent;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IDiscoveryBackend"/>.
/// Suitable for monolith (single-process) deployments and integration tests.
/// All registered topologies live for the lifetime of the application.
/// </summary>
public sealed class InMemoryDiscoveryBackend : IDiscoveryBackend
{
    private readonly ConcurrentDictionary<string, HostTopologyInfo> _topologies = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task StoreAsync(HostTopologyInfo topology, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topology.HostName))
            throw new ArgumentException("topology.HostName must not be null or empty.", nameof(topology));

        _topologies[topology.HostName] = topology;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<HostTopologyInfo?> GetByHostNameAsync(string hostName, CancellationToken ct = default)
    {
        _topologies.TryGetValue(hostName, out var topology);
        return Task.FromResult(topology);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HostTopologyInfo>> GetAllAsync(CancellationToken ct = default)
    {
        // Snapshot the values with a pre-sized list to avoid internal resize allocations.
        var snapshot = new List<HostTopologyInfo>(_topologies.Count);
        snapshot.AddRange(_topologies.Values);
        return Task.FromResult<IReadOnlyList<HostTopologyInfo>>(snapshot);
    }
}
