using Pragmatic.Authorization.Catalog;
using Pragmatic.Authorization.Serialization;

namespace Pragmatic.Authorization.Stores;

/// <summary>
///     Store for resource-level policy assignments (e.g., dynamic policy ↔ resource mapping).
///     Implementations: EfDynamicResourceStore (Phase 3).
/// </summary>
public interface IResourcePolicyStore
{
    /// <summary>Gets all dynamically registered protected resources.</summary>
    ValueTask<IReadOnlyList<ProtectedResource>> GetAllResourcesAsync(CancellationToken ct = default);

    /// <summary>Gets the policy expression assigned to a specific resource.</summary>
    ValueTask<PolicyExpression?> GetPolicyAsync(string resourceIdentifier, CancellationToken ct = default);
}
