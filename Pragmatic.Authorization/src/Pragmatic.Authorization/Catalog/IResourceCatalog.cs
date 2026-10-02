using Pragmatic.Authorization.Serialization;

namespace Pragmatic.Authorization.Catalog;

/// <summary>
///     Unified catalog of protected resources, merging static (SG-generated)
///     and dynamic (database-backed) registrations.
/// </summary>
public interface IResourceCatalog
{
    /// <summary>
    ///     Gets all known protected resources.
    /// </summary>
    ValueTask<IReadOnlyList<ProtectedResource>> GetAllResourcesAsync(CancellationToken ct = default);

    /// <summary>
    ///     Gets the policy expression assigned to a specific resource.
    /// </summary>
    /// <param name="resourceIdentifier">The resource identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<PolicyExpression?> GetResourcePolicyAsync(string resourceIdentifier, CancellationToken ct = default);
}
