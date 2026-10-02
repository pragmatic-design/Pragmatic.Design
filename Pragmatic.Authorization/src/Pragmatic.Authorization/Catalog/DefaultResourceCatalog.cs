using Pragmatic.Authorization.Serialization;
using Pragmatic.Authorization.Stores;

namespace Pragmatic.Authorization.Catalog;

/// <summary>
///     Default catalog that merges SG-generated static resource registrations
///     with optional dynamic policy store (database-backed).
/// </summary>
/// <remarks>
///     Static resources are provided via DI as <see cref="IReadOnlyList{ProtectedResource}"/>.
///     There is currently no source generator that materializes a static resource registry, so this
///     list is empty unless registered manually. Resource-level authorization is instead wired at
///     runtime via <c>AddResourceAuthorizer&lt;T&gt;</c> and dynamic policy assignments come from
///     <see cref="IResourcePolicyStore"/>.
/// </remarks>
public sealed class DefaultResourceCatalog : IResourceCatalog
{
    private readonly IResourcePolicyStore? _dynamicStore;
    // O(1) dictionary lookup instead of O(n) linear scan on every auth check.
    private readonly Dictionary<string, ProtectedResource> _staticIndex;
    private readonly IReadOnlyList<ProtectedResource> _staticList;

    public DefaultResourceCatalog(
        IReadOnlyList<ProtectedResource>? staticResources = null,
        IResourcePolicyStore? dynamicStore = null)
    {
        _dynamicStore = dynamicStore;
        _staticList = staticResources ?? [];
        _staticIndex = new Dictionary<string, ProtectedResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in _staticList)
        {
            // A plain ToDictionary would throw the opaque "same key" error; surface which identifier
            // collides (case-insensitively) so the misconfiguration is actionable at startup.
            if (!_staticIndex.TryAdd(resource.Identifier, resource))
                throw new ArgumentException(
                    $"Duplicate protected-resource identifier '{resource.Identifier}' (case-insensitive). " +
                    "Each ProtectedResource must have a unique identifier.",
                    nameof(staticResources));
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ProtectedResource>> GetAllResourcesAsync(CancellationToken ct = default)
    {
        var result = new List<ProtectedResource>(_staticList);

        if (_dynamicStore is not null)
        {
            var dynamic = await _dynamicStore.GetAllResourcesAsync(ct).ConfigureAwait(false);
            result.AddRange(dynamic);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<PolicyExpression?> GetResourcePolicyAsync(
        string resourceIdentifier, CancellationToken ct = default)
    {
        // O(1) dictionary lookup for static resources.
        if (_staticIndex.TryGetValue(resourceIdentifier, out var staticResource)
            && staticResource.PolicyExpression is not null)
            return staticResource.PolicyExpression;

        // Fall back to dynamic store
        if (_dynamicStore is not null)
            return await _dynamicStore.GetPolicyAsync(resourceIdentifier, ct).ConfigureAwait(false);

        return null;
    }
}
