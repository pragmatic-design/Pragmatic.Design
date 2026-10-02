// Pragmatic.Discovery - Discovery Validator

using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Services;

/// <summary>
/// Validates cross-host topology coherence.
/// Called when a new host registers itself to detect conflicts with already-registered hosts.
/// </summary>
internal sealed class DiscoveryValidator
{
    /// <summary>
    /// Validates <paramref name="incoming"/> against all <paramref name="existing"/> registered topologies.
    /// </summary>
    /// <returns>A <see cref="DiscoveryValidationResult"/> with any issues found.</returns>
    public static DiscoveryValidationResult Validate(HostTopologyInfo incoming, IReadOnlyList<HostTopologyInfo> existing)
    {
        var issues = new List<DiscoveryValidationIssue>();

        foreach (var host in existing)
        {
            // Skip self-comparison (re-registration)
            if (string.Equals(host.HostName, incoming.HostName, StringComparison.OrdinalIgnoreCase))
                continue;

            ValidateModuleConflicts(incoming, host, issues);
        }

        var isValid = !HasErrors(issues);
        return new DiscoveryValidationResult { IsValid = isValid, Issues = issues };
    }

    /// <summary>
    /// DISC001: Same module deployed in multiple hosts on different databases.
    /// This is allowed (scale-out) but flagged as Info when databases differ.
    /// DISC002: Same module + same database → conflict if providers differ.
    /// </summary>
    private static void ValidateModuleConflicts(
        HostTopologyInfo incoming,
        HostTopologyInfo existing,
        List<DiscoveryValidationIssue> issues)
    {
        // Pre-build a dictionary from the existing host's modules for O(1) lookup.
        var existingByName = new Dictionary<string, ModuleDeploymentInfo>(existing.Modules.Count, StringComparer.Ordinal);
        foreach (var m in existing.Modules)
            existingByName.TryAdd(m.ModuleName, m);

        foreach (var incomingModule in incoming.Modules)
        {
            if (!existingByName.TryGetValue(incomingModule.ModuleName, out var existingModule))
                continue;

            // Same module, different database → multi-deployment (allowed, info only)
            if (!string.Equals(existingModule.DatabaseName, incomingModule.DatabaseName, StringComparison.Ordinal))
            {
                issues.Add(new DiscoveryValidationIssue
                {
                    Code = "DISC001",
                    Severity = IssueSeverity.Info,
                    Message = $"Module '{incomingModule.ModuleName}' is deployed in both '{incoming.HostName}' " +
                              $"(database: {incomingModule.DatabaseName ?? "none"}) and '{existing.HostName}' " +
                              $"(database: {existingModule.DatabaseName ?? "none"}). " +
                              "Ensure they share the same physical database or use a messaging layer for consistency."
                });
                continue;
            }

            // Same module, same database name, different providers → misconfiguration: the same
            // logical database cannot be served by two different EF providers (SQL dialect mismatch).
            // Error severity so an operator who opts into ThrowOnValidationFailure is actually blocked
            // (the default, false, only logs). Without an Error-severity issue the option would be inert.
            if (!string.Equals(existingModule.Provider, incomingModule.Provider, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new DiscoveryValidationIssue
                {
                    Code = "DISC002",
                    Severity = IssueSeverity.Error,
                    Message = $"Module '{incomingModule.ModuleName}' is deployed on database '{incomingModule.DatabaseName}' " +
                              $"with provider '{incomingModule.Provider}' in '{incoming.HostName}' but " +
                              $"provider '{existingModule.Provider}' in '{existing.HostName}'. " +
                              "The same database cannot use two different providers."
                });
            }
        }
    }

    private static bool HasErrors(List<DiscoveryValidationIssue> issues)
    {
        foreach (var issue in issues)
            if (issue.Severity == IssueSeverity.Error)
                return true;
        return false;
    }

    // No runtime ReadAccess→owning-module check: DISC003 is retired and not reused. The topology
    // metadata carries no entity→module ownership map, and matching entity names against module names
    // cannot work (entities are simple names like "Property", modules are suffixed like "CatalogModule"),
    // so such a check fires for every boundary — pure noise. Compile-time PRAG0601/PRAG0602 are the
    // authoritative check.
}
