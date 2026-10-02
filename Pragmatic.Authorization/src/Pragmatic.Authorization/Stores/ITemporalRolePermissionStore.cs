namespace Pragmatic.Authorization.Stores;

/// <summary>
///     Extends <see cref="IRolePermissionStore"/> with point-in-time permission resolution.
///     Forward-compatible interface for L3 temporal authorization.
/// </summary>
public interface ITemporalRolePermissionStore : IRolePermissionStore
{
    /// <summary>Gets permissions for a role as they were at the specified point in time.</summary>
    ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, DateTimeOffset asOf, CancellationToken ct = default);
}
