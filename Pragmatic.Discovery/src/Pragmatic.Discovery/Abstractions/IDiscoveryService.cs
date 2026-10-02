// Pragmatic.Discovery - Discovery Service Abstraction

using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Abstractions;

/// <summary>
/// High-level service for host topology registration and cross-host discovery.
/// </summary>
/// <remarks>
/// In a monolith (InMemory backend), discovery is self-referential.
/// In a distributed deployment, each host registers at startup and can discover others by module name.
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]
public interface IDiscoveryService
{
    /// <summary>
    /// Registers this host's topology in the discovery registry.
    /// Called automatically at startup when <see cref="Options.DiscoveryOptions.AutoRegisterOnStartup"/> is true.
    /// </summary>
    Task RegisterAsync(HostTopologyInfo topology, CancellationToken ct = default);

    /// <summary>Returns all currently registered host topologies.</summary>
    Task<IReadOnlyList<HostTopologyInfo>> GetAllHostsAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds all hosts that include (own) the specified module.
    /// Useful for locating which host exposes a given boundary in a distributed system.
    /// </summary>
    Task<IReadOnlyList<HostTopologyInfo>> FindHostsForModuleAsync(string moduleName, CancellationToken ct = default);

    /// <summary>
    /// Validates the given topology against the current registry state.
    /// Checks for module ownership conflicts and cross-host coherence issues.
    /// </summary>
    Task<DiscoveryValidationResult> ValidateAsync(HostTopologyInfo localTopology, CancellationToken ct = default);
}
