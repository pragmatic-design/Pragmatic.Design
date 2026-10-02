namespace Pragmatic.Authorization;

/// <summary>
///     Async permission checker for scenarios requiring external authorization lookups
///     (e.g., policy servers, database-backed RBAC).
/// </summary>
/// <remarks>
///     Use <see cref="IUserAuthorization" /> for simple in-memory checks (claims/roles).
///     Use <see cref="IPermissionChecker" /> when authorization requires I/O (API calls, DB queries).
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IPermissionChecker
{
    /// <summary>
    ///     Checks whether the current user has the specified permission.
    /// </summary>
    ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Checks whether the current user has at least one of the specified permissions (OR logic).
    /// </summary>
    ValueTask<bool> HasAnyPermissionAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Checks whether the current user has all of the specified permissions (AND logic).
    /// </summary>
    ValueTask<bool> HasAllPermissionsAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default);
}
