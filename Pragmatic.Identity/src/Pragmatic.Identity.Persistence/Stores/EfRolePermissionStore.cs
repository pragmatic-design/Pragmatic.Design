using Microsoft.EntityFrameworkCore;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity.Persistence.Entities;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Persistence.Stores;

/// <summary>
///     EF Core implementation of <see cref="ITemporalRolePermissionStore"/>.
///     Filters by temporal validity using <see cref="IClock"/>.
/// </summary>
public sealed class EfRolePermissionStore(
    DbContext dbContext,
    IClock clock) : ITemporalRolePermissionStore
{
    private DbSet<RolePermission> RolePermissions => dbContext.Set<RolePermission>();

    /// <inheritdoc />
    public ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, CancellationToken ct = default)
        => GetPermissionsForRoleAsync(roleName, clock.UtcNow, ct);

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> GetPermissionsForRoleAsync(
        string roleName, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var permissions = await RolePermissions
            .Where(rp => rp.RoleName == roleName
                && rp.ValidFrom <= asOf
                && (rp.ValidTo == null || rp.ValidTo > asOf))
            .Select(rp => rp.PermissionName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetAllRolesAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var roles = await RolePermissions
            .Where(rp => rp.ValidFrom <= now
                && (rp.ValidTo == null || rp.ValidTo > now))
            .Select(rp => rp.RoleName)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return roles;
    }
}
