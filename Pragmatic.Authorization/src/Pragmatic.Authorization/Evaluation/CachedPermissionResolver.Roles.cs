namespace Pragmatic.Authorization.Evaluation;

/// <summary>
///     The caller's roles: the identity's <c>role</c> claims, joined to what the <see cref="IRoleProvider" />s resolve.
/// </summary>
public sealed partial class CachedPermissionResolver
{
    private readonly IReadOnlyList<IRoleProvider> _roleProviders;
    private IReadOnlyCollection<string>? _resolvedRoles;

    // The same guard as the permissions': a role provider that reads through a filtered repository asks for the caller's permissions,
    // whose cache tags ask for the roles. The reentrant ask gets the identity's roles alone, which is what it got before role providers
    // existed, and the outer resolution still stores the whole answer.
    private bool _resolvingRoles;

    /// <inheritdoc />
    /// <remarks>
    ///     Resolved once per scope. Before #723 these were the claims alone, so a role assigned in a store granted its permissions and
    ///     failed every role check.
    /// </remarks>
    public IReadOnlyCollection<string> Roles => _resolvedRoles ?? ResolveRoles();

    private IReadOnlyCollection<string> ClaimedRoles => _currentUser.Claims.TryGetValue("role", out var roles)
        ? (IReadOnlyCollection<string>)roles
        : Array.Empty<string>();

    private IReadOnlyCollection<string> ResolveRoles()
    {
        if (_roleProviders.Count == 0)
            return _resolvedRoles = ClaimedRoles;
        if (_resolvingRoles)
            return ClaimedRoles;

        _resolvingRoles = true;
        try
        {
            var roles = new HashSet<string>(ClaimedRoles, StringComparer.OrdinalIgnoreCase);
            foreach (var provider in _roleProviders)
            {
                var task = provider.ResolveRolesAsync(_currentUser);
                // As the permission providers: Task.Run detaches from an ambient SynchronizationContext before blocking.
                var resolved = task.IsCompleted ? task.Result : Task.Run(() => task.AsTask()).GetAwaiter().GetResult();
                roles.UnionWith(resolved);
            }

            return _resolvedRoles = roles;
        }
        finally
        {
            _resolvingRoles = false;
        }
    }
}
