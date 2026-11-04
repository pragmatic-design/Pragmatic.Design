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
}
