using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Persistence.Documents;

namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>
///     The package's rows on the documents engine, through the session of the boundary that imported it: what
///     <c>EfAuthorizationManagementStore</c> does, one document per row.
/// </summary>
public sealed class DocAuthorizationManagementStore : IAuthorizationManagementStore
{
    private readonly DocSession _session;

    internal DocAuthorizationManagementStore(DocSession session) => _session = session;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicRole>> GetRolesAsync(string? tenantId, CancellationToken ct = default)
    {
        var documents = await _session.Query<DynamicRoleDocument>()
            .Where((r, t) => !r.IsDeleted && (r.TenantId == null || r.TenantId == t), tenantId)
            .ToListAsync(ct).ConfigureAwait(false);
        return documents.ConvertAll(d => d.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask AddRoleAsync(DynamicRole role, CancellationToken ct = default)
        => await _session.InsertAsync(DynamicRoleDocument.From(role), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicPermission>> GetPermissionsAsync(string? tenantId, CancellationToken ct = default)
    {
        var documents = await _session.Query<DynamicPermissionDocument>()
            .Where((p, t) => !p.IsDeleted && (p.TenantId == null || p.TenantId == t), tenantId)
            .ToListAsync(ct).ConfigureAwait(false);
        return documents.ConvertAll(d => d.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask AddPermissionAsync(DynamicPermission permission, CancellationToken ct = default)
        => await _session.InsertAsync(DynamicPermissionDocument.From(permission), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicRolePermission>> GetRolePermissionsAsync(
        IReadOnlyCollection<string> roleNames, string? tenantId, CancellationToken ct = default)
    {
        if (roleNames.Count == 0)
            return [];

        var documents = await _session.Query<DynamicRolePermissionDocument>()
            .Where((rp, k) => k.Roles.Contains(rp.RoleName) && (rp.TenantId == null || rp.TenantId == k.Tenant), (Roles: roleNames.ToArray(), Tenant: tenantId))
            .ToListAsync(ct).ConfigureAwait(false);
        return documents.ConvertAll(d => d.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask AddRolePermissionsAsync(IReadOnlyList<DynamicRolePermission> grants, CancellationToken ct = default)
    {
        if (grants.Count == 0)
            return;

        await _session.InsertManyAsync(grants.Select(DynamicRolePermissionDocument.From).ToList(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserRoleAssignment>> GetUserRolesAsync(string userId, string? tenantId, CancellationToken ct = default)
    {
        var documents = await _session.Query<UserRoleAssignmentDocument>()
            .Where((a, k) => a.UserId == k.User && (a.TenantId == null || a.TenantId == k.Tenant), (User: userId, Tenant: tenantId))
            .ToListAsync(ct).ConfigureAwait(false);
        return documents.ConvertAll(d => d.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserRoleAssignment>> GetRoleHoldersAsync(string roleName, CancellationToken ct = default)
    {
        var documents = await _session.Query<UserRoleAssignmentDocument>()
            .Where((a, role) => a.RoleName == role, roleName)
            .ToListAsync(ct).ConfigureAwait(false);
        return documents.ConvertAll(d => d.ToEntity());
    }

    /// <inheritdoc />
    public async ValueTask AddUserRoleAsync(UserRoleAssignment assignment, CancellationToken ct = default)
        => await _session.InsertAsync(UserRoleAssignmentDocument.From(assignment), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask EndUserRoleAsync(UserRoleAssignment assignment, DateTimeOffset validTo, CancellationToken ct = default)
    {
        assignment.ValidTo = validTo;
        await _session.ReplaceAsync(UserRoleAssignmentDocument.From(assignment), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserGroupAssignment>> GetUserGroupsAsync(string userId, string? tenantId, CancellationToken ct = default)
    {
        var documents = await _session.Query<UserGroupAssignmentDocument>()
            .Where((a, k) => a.UserId == k.User && (a.TenantId == null || a.TenantId == k.Tenant), (User: userId, Tenant: tenantId))
            .ToListAsync(ct).ConfigureAwait(false);
        return documents.ConvertAll(d => d.ToEntity());
    }
}
