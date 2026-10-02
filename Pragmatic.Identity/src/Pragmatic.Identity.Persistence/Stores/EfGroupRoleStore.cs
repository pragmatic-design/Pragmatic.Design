using Microsoft.EntityFrameworkCore;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity.Persistence.Entities;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Identity.Persistence.Stores;

/// <summary>
///     EF Core implementation of <see cref="ITemporalGroupRoleStore"/>.
///     Filters by temporal validity using <see cref="IClock"/>.
/// </summary>
public sealed class EfGroupRoleStore(
    DbContext dbContext,
    IClock clock) : ITemporalGroupRoleStore
{
    private DbSet<GroupRole> GroupRoles => dbContext.Set<GroupRole>();

    /// <inheritdoc />
    public ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(
        string groupName, CancellationToken ct = default)
        => GetRolesForGroupAsync(groupName, clock.UtcNow, ct);

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<string>> GetRolesForGroupAsync(
        string groupName, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var roles = await GroupRoles
            .Where(gr => gr.GroupName == groupName
                && gr.ValidFrom <= asOf
                && (gr.ValidTo == null || gr.ValidTo > asOf))
            .Select(gr => gr.RoleName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> GetAllGroupsAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var groups = await GroupRoles
            .Where(gr => gr.ValidFrom <= now
                && (gr.ValidTo == null || gr.ValidTo > now))
            .Select(gr => gr.GroupName)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return groups;
    }
}
