namespace Pragmatic.Authorization.Evaluation;

/// <summary>
///     Invalidates cached permission sets so that authorization changes propagate
///     immediately, without waiting for the cache entry to expire.
/// </summary>
/// <remarks>
///     Registered when cross-request permission caching is enabled
///     (<c>AuthorizationBuilder.UsePermissionCache</c>). The application is expected
///     to call this whenever a user's roles, permissions, or active status change —
///     this is the <see cref="Configuration.PermissionCacheStrategy.ManualInvalidation"/>
///     strategy. With <see cref="Configuration.PermissionCacheStrategy.TimeToLive"/> the
///     entries expire on their own and calling the invalidator is optional.
/// </remarks>
public interface IPermissionCacheInvalidator
{
    /// <summary>
    ///     Drops the cached permission set for a single user across all tenants.
    ///     Call after changing that user's roles/permissions or after disabling the account.
    /// </summary>
    ValueTask InvalidateUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    ///     Drops the cached permission set for every user in a tenant.
    ///     Call after a tenant-wide role or permission change.
    /// </summary>
    ValueTask InvalidateTenantAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    ///     Drops the cached permission set for every user who holds the given role — whether directly
    ///     (a <c>role</c> claim) or transitively through a group that grants it. Call after changing a
    ///     role's permission set. Direct holders are reached via the <c>role:{name}</c> tag; group-based
    ///     holders are reached by reverse-looking-up the groups that grant the role and dropping their
    ///     <c>group:{name}</c> tags, so no affected user is left with a stale entry.
    /// </summary>
    ValueTask InvalidateRoleAsync(string roleName, CancellationToken ct = default);

    /// <summary>
    ///     Drops the cached permission set for every user who is a member of the given group.
    ///     Call after changing the roles assigned to a group — cached entries are tagged by the
    ///     groups that contributed to them.
    /// </summary>
    ValueTask InvalidateGroupAsync(string groupName, CancellationToken ct = default);
}
