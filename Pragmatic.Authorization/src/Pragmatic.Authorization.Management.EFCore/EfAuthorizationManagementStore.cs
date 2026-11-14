using Microsoft.EntityFrameworkCore;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;

namespace Pragmatic.Authorization.Management.EFCore;

/// <summary>
///     The package's rows on EF Core, through the context of the boundary that imported it: the importing boundary's context maps the
///     package's entities, so the tables are its own.
/// </summary>
public sealed class EfAuthorizationManagementStore : IAuthorizationManagementStore
{
    private readonly DbContext _dbContext;

    internal EfAuthorizationManagementStore(DbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicRole>> GetRolesAsync(string? tenantId, CancellationToken ct = default)
        => await _dbContext.Set<DynamicRole>()
            .Where(r => !r.IsDeleted && (r.TenantId == null || r.TenantId == tenantId))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask AddRoleAsync(DynamicRole role, CancellationToken ct = default)
    {
        _dbContext.Set<DynamicRole>().Add(role);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask UpdateRoleAsync(DynamicRole role, CancellationToken ct = default)
    {
        // Read through this context, the role is tracked; one built elsewhere is attached and written whole.
        if (_dbContext.Entry(role).State == EntityState.Detached)
            _dbContext.Update(role);

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicPermission>> GetPermissionsAsync(string? tenantId, CancellationToken ct = default)
        => await _dbContext.Set<DynamicPermission>()
            .Where(p => !p.IsDeleted && (p.TenantId == null || p.TenantId == tenantId))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask AddPermissionAsync(DynamicPermission permission, CancellationToken ct = default)
    {
        _dbContext.Set<DynamicPermission>().Add(permission);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicRolePermission>> GetRolePermissionsAsync(
        IReadOnlyCollection<string> roleNames, string? tenantId, CancellationToken ct = default)
    {
        if (roleNames.Count == 0)
            return [];

        var names = roleNames.ToList();
        return await _dbContext.Set<DynamicRolePermission>()
            .Where(rp => names.Contains(rp.RoleName) && (rp.TenantId == null || rp.TenantId == tenantId))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask AddRolePermissionsAsync(IReadOnlyList<DynamicRolePermission> grants, CancellationToken ct = default)
    {
        if (grants.Count == 0)
            return;

        _dbContext.Set<DynamicRolePermission>().AddRange(grants);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask EndRolePermissionsAsync(IReadOnlyList<DynamicRolePermission> grants, DateTimeOffset validTo, CancellationToken ct = default)
    {
        if (grants.Count == 0)
            return;

        // As EndUserRoleAsync: a grant read here is tracked, one built elsewhere is attached so only ValidTo is written.
        foreach (var grant in grants)
        {
            if (_dbContext.Entry(grant).State == EntityState.Detached)
                _dbContext.Attach(grant);
            grant.ValidTo = validTo;
        }

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserRoleAssignment>> GetUserRolesAsync(string userId, string? tenantId, CancellationToken ct = default)
        => await _dbContext.Set<UserRoleAssignment>()
            .Where(a => a.UserId == userId && (a.TenantId == null || a.TenantId == tenantId))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserRoleAssignment>> GetRoleHoldersAsync(string roleName, CancellationToken ct = default)
        => await _dbContext.Set<UserRoleAssignment>()
            .Where(a => a.RoleName == roleName)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask AddUserRoleAsync(UserRoleAssignment assignment, CancellationToken ct = default)
    {
        _dbContext.Set<UserRoleAssignment>().Add(assignment);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask EndUserRoleAsync(UserRoleAssignment assignment, DateTimeOffset validTo, CancellationToken ct = default)
    {
        // Read through this context, the assignment is tracked; one built elsewhere is attached, so only ValidTo is written.
        if (_dbContext.Entry(assignment).State == EntityState.Detached)
            _dbContext.Attach(assignment);

        assignment.ValidTo = validTo;
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserGroupAssignment>> GetUserGroupsAsync(string userId, string? tenantId, CancellationToken ct = default)
        => await _dbContext.Set<UserGroupAssignment>()
            .Where(a => a.UserId == userId && (a.TenantId == null || a.TenantId == tenantId))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UserGroupAssignment>> GetGroupMembersAsync(string groupName, CancellationToken ct = default)
        => await _dbContext.Set<UserGroupAssignment>()
            .Where(a => a.GroupName == groupName)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask AddUserGroupAsync(UserGroupAssignment assignment, CancellationToken ct = default)
    {
        _dbContext.Set<UserGroupAssignment>().Add(assignment);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask EndUserGroupAsync(UserGroupAssignment assignment, DateTimeOffset validTo, CancellationToken ct = default)
    {
        // As EndUserRoleAsync: tracked when read here, attached when built elsewhere, so only ValidTo is written.
        if (_dbContext.Entry(assignment).State == EntityState.Detached)
            _dbContext.Attach(assignment);

        assignment.ValidTo = validTo;
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DynamicGroup>> GetGroupsAsync(string? tenantId, CancellationToken ct = default)
        => await _dbContext.Set<DynamicGroup>()
            .Where(g => !g.IsDeleted && (g.TenantId == null || g.TenantId == tenantId))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask AddGroupAsync(DynamicGroup group, CancellationToken ct = default)
    {
        _dbContext.Set<DynamicGroup>().Add(group);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<GroupRoleAssignment>> GetGroupRolesAsync(
        IReadOnlyCollection<string> groupNames, string? tenantId, CancellationToken ct = default)
    {
        if (groupNames.Count == 0)
            return [];

        var names = groupNames.ToList();
        return await _dbContext.Set<GroupRoleAssignment>()
            .Where(gr => names.Contains(gr.GroupName) && (gr.TenantId == null || gr.TenantId == tenantId))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<GroupRoleAssignment>> GetRoleGroupsAsync(string roleName, CancellationToken ct = default)
        => await _dbContext.Set<GroupRoleAssignment>()
            .Where(gr => gr.RoleName == roleName)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask AddGroupRoleAsync(GroupRoleAssignment assignment, CancellationToken ct = default)
    {
        _dbContext.Set<GroupRoleAssignment>().Add(assignment);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask EndGroupRoleAsync(GroupRoleAssignment assignment, DateTimeOffset validTo, CancellationToken ct = default)
    {
        if (_dbContext.Entry(assignment).State == EntityState.Detached)
            _dbContext.Attach(assignment);

        assignment.ValidTo = validTo;
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
