using Pragmatic.Authorization;

namespace Pragmatic.Identity;

/// <summary>
///     Default <see cref="IPermissionChecker" /> that delegates to <see cref="ICurrentUser" />.
///     Wraps synchronous claims-based checks as async for consumers that require <see cref="IPermissionChecker" />.
/// </summary>
/// <remarks>
///     Replace with a database-backed or policy server implementation when
///     authorization requires external lookups beyond in-memory claims.
/// </remarks>
public sealed class ClaimsPermissionChecker(ICurrentUser currentUser) : IPermissionChecker
{
    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(currentUser.Authorization.HasPermission(permission));

    /// <inheritdoc />
    public ValueTask<bool> HasAnyPermissionAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(currentUser.Authorization.HasAnyPermission(permissions));

    /// <inheritdoc />
    public ValueTask<bool> HasAllPermissionsAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(currentUser.Authorization.HasAllPermissions(permissions));
}
