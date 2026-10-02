// Pragmatic.Discovery - Discovery Backend Abstraction

using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Abstractions;

/// <summary>
/// Storage backend for the discovery registry.
/// Implement this interface to plug in a different backend (Redis, Consul, HTTP, etc.).
/// The default implementation is <c>InMemoryDiscoveryBackend</c> — suitable for
/// monolith and single-process deployments.
/// </summary>
public interface IDiscoveryBackend
{
    /// <summary>
    /// Stores (or updates) a host topology entry. Semantics are upsert: calling this method
    /// with the same <c>topology.HostName</c> replaces any previously stored entry.
    /// Implementations must never throw on a duplicate host name — re-registration is always safe.
    /// </summary>
    Task StoreAsync(HostTopologyInfo topology, CancellationToken ct = default);

    /// <summary>Retrieves the topology for a specific host by name, or null if not registered.</summary>
    Task<HostTopologyInfo?> GetByHostNameAsync(string hostName, CancellationToken ct = default);

    /// <summary>Returns all registered host topologies.</summary>
    Task<IReadOnlyList<HostTopologyInfo>> GetAllAsync(CancellationToken ct = default);
}
