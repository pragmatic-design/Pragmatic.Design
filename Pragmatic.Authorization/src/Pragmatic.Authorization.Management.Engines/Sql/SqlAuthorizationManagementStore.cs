using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>
///     The package's rows on the Sql engine, through the session of the boundary that imported it: what
///     <c>EfAuthorizationManagementStore</c> does, on the same tables.
/// </summary>
public sealed class SqlAuthorizationManagementStore : IAuthorizationManagementStore
{
    private readonly SqlSession _session;

    internal SqlAuthorizationManagementStore(SqlSession session) => _session = session;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicRole>> GetRolesAsync(string? tenantId, CancellationToken ct = default)
    {
        var rows = await _session.Query<DynamicRoleRow>()
            .Where((r, t) => !r.IsDeleted && (r.TenantId == null || r.TenantId == t), tenantId)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.ConvertAll(r => r.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask AddRoleAsync(DynamicRole role, CancellationToken ct = default)
        => await _session.InsertAsync(DynamicRoleRow.From(role), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicPermission>> GetPermissionsAsync(string? tenantId, CancellationToken ct = default)
    {
        var rows = await _session.Query<DynamicPermissionRow>()
            .Where((p, t) => !p.IsDeleted && (p.TenantId == null || p.TenantId == t), tenantId)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.ConvertAll(r => r.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask AddPermissionAsync(DynamicPermission permission, CancellationToken ct = default)
        => await _session.InsertAsync(DynamicPermissionRow.From(permission), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicRolePermission>> GetRolePermissionsAsync(
        IReadOnlyCollection<string> roleNames, string? tenantId, CancellationToken ct = default)
    {
        if (roleNames.Count == 0)
            return [];

        var rows = await _session.Query<DynamicRolePermissionRow>()
            .Where((rp, k) => k.Roles.Contains(rp.RoleName) && (rp.TenantId == null || rp.TenantId == k.Tenant), (Roles: roleNames.ToArray(), Tenant: tenantId))
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.ConvertAll(r => r.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask AddRolePermissionsAsync(IReadOnlyList<DynamicRolePermission> grants, CancellationToken ct = default)
    {
        if (grants.Count == 0)
            return;

        await _session.InsertManyAsync(grants.Select(DynamicRolePermissionRow.From).ToList(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserRoleAssignment>> GetUserRolesAsync(string userId, string? tenantId, CancellationToken ct = default)
    {
        var rows = await _session.Query<UserRoleAssignmentRow>()
            .Where((a, k) => a.UserId == k.User && (a.TenantId == null || a.TenantId == k.Tenant), (User: userId, Tenant: tenantId))
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.ConvertAll(r => r.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserRoleAssignment>> GetRoleHoldersAsync(string roleName, CancellationToken ct = default)
    {
        var rows = await _session.Query<UserRoleAssignmentRow>()
            .Where((a, role) => a.RoleName == role, roleName)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.ConvertAll(r => r.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask AddUserRoleAsync(UserRoleAssignment assignment, CancellationToken ct = default)
        => await _session.InsertAsync(UserRoleAssignmentRow.From(assignment), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask EndUserRoleAsync(UserRoleAssignment assignment, DateTimeOffset validTo, CancellationToken ct = default)
    {
        assignment.ValidTo = validTo;
        await _session.UpdateAsync(UserRoleAssignmentRow.From(assignment), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserGroupAssignment>> GetUserGroupsAsync(string userId, string? tenantId, CancellationToken ct = default)
    {
        var rows = await _session.Query<UserGroupAssignmentRow>()
            .Where((a, k) => a.UserId == k.User && (a.TenantId == null || a.TenantId == k.Tenant), (User: userId, Tenant: tenantId))
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.ConvertAll(r => r.ToEntity());
    }
}
