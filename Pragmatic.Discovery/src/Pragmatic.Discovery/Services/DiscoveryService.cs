// Pragmatic.Discovery - Discovery Service

using Microsoft.Extensions.Logging;
using Pragmatic.Discovery.Abstractions;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Services;

/// <summary>
/// Default implementation of <see cref="IDiscoveryService"/>.
/// Delegates persistence to <see cref="IDiscoveryBackend"/> and validation to <see cref="DiscoveryValidator"/>.
/// </summary>
internal sealed class DiscoveryService(
    IDiscoveryBackend backend,
    ILogger<DiscoveryService> logger) : IDiscoveryService
{
    /// <inheritdoc/>
    public async Task RegisterAsync(HostTopologyInfo topology, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Registering host topology: {HostName} with {ModuleCount} module(s)",
            topology.HostName,
            topology.Modules.Count);

        await backend.StoreAsync(topology, ct).ConfigureAwait(false);

        logger.LogDebug(
            "Host '{HostName}' registered successfully at {RegisteredAt}",
            topology.HostName,
            topology.RegisteredAt);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HostTopologyInfo>> GetAllHostsAsync(CancellationToken ct = default)
        => await backend.GetAllAsync(ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HostTopologyInfo>> FindHostsForModuleAsync(string moduleName, CancellationToken ct = default)
    {
        // NOTE: fetches all hosts and filters in-memory — acceptable for InMemoryDiscoveryBackend.
        // Remote backends (Redis/Consul) should override IDiscoveryBackend with a server-side filtered query
        // to avoid an O(n) network round-trip on every call.
        var all = await backend.GetAllAsync(ct).ConfigureAwait(false);
        var result = new List<HostTopologyInfo>();
        foreach (var host in all)
            foreach (var module in host.Modules)
                if (string.Equals(module.ModuleName, moduleName, StringComparison.Ordinal))
                {
                    result.Add(host);
                    break;
                }
        return result;
    }

    /// <inheritdoc/>
    public async Task<DiscoveryValidationResult> ValidateAsync(HostTopologyInfo localTopology, CancellationToken ct = default)
    {
        var existing = await backend.GetAllAsync(ct).ConfigureAwait(false);
        var result = DiscoveryValidator.Validate(localTopology, existing);

        if (result.IsValid)
        {
            logger.LogInformation(
                "Topology validation passed for host '{HostName}'. Warnings: {WarningCount}",
                localTopology.HostName,
                result.Issues.Count(i => i.Severity == IssueSeverity.Warning));
        }
        else
        {
            foreach (var error in result.Errors)
                logger.LogError("[{Code}] {Message}", error.Code, error.Message);
        }

        foreach (var warning in result.Warnings)
            logger.LogWarning("[{Code}] {Message}", warning.Code, warning.Message);

        return result;
    }
}
