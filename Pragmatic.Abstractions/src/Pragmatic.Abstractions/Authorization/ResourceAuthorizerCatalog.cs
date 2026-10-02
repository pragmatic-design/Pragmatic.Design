namespace Pragmatic.Authorization;

/// <summary>
///     Default <see cref="IResourceAuthorizerCatalog" />: a plain set of resource types, filled in as
///     authorizers are registered.
/// </summary>
/// <remarks>
///     Registered as a singleton by <c>AuthorizationBuilder.AddResourceAuthorizer</c>. Writes happen
///     while the service collection is being built and reads happen for the lifetime of the app, so both
///     go through the same lock rather than relying on the two phases never overlapping.
/// </remarks>
public sealed class ResourceAuthorizerCatalog : IResourceAuthorizerCatalog
{
    private readonly HashSet<Type> _resourceTypes = [];
    private readonly Lock _gate = new();

    /// <summary>Creates a catalog covering <paramref name="resourceTypes" /> (empty when none are given).</summary>
    /// <param name="resourceTypes">The resource types to record.</param>
    public ResourceAuthorizerCatalog(params Type[] resourceTypes)
    {
        ArgumentNullException.ThrowIfNull(resourceTypes);

        foreach (var type in resourceTypes)
            Add(type);
    }

    /// <summary>Records that an authorizer was registered for <paramref name="resourceType" />.</summary>
    /// <param name="resourceType">The resource type the authorizer protects.</param>
    public void Add(Type resourceType)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        lock (_gate)
            _resourceTypes.Add(resourceType);
    }

    /// <inheritdoc />
    public bool Covers(Type resourceType)
    {
        if (resourceType is null)
            return false;

        lock (_gate)
            return _resourceTypes.Contains(resourceType);
    }
}
