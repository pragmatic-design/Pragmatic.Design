namespace Pragmatic.Authorization.Stores;

/// <summary>
///     Extends <see cref="IGroupRoleStore"/> with tenant-specific role resolution.
///     Forward-compatible interface for L3 multi-tenant authorization.
/// </summary>
public interface ITenantGroupRoleStore : IGroupRoleStore
{
    /// <summary>Gets roles for a group within a specific tenant context.</summary>
    ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(
        string groupName, string tenantId, CancellationToken ct = default);
}
