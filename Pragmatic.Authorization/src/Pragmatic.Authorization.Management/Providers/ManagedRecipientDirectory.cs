using System.Runtime.CompilerServices;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Composition.Attributes;
using Pragmatic.Notifications;

namespace Pragmatic.Authorization.Management.Providers;

/// <summary>
///     Who holds a role this package assigns, for a notification addressed to the role (#743): the users it is assigned to, and the members of
///     the groups it is given to, in force now, in the tenant asked or global. It knows no user's address: that is another directory's.
/// </summary>
/// <remarks>
///     The same rule as <see cref="ManagedRoleProvider" />: a deactivated role is held by nobody, and an assignment, a membership or a group's
///     role counts only while it is in force and is of the tenant asked or global. A user who holds the role twice (directly and through a
///     group) is named once.
/// </remarks>
/// <param name="store">The rows of the boundary that imported the package.</param>
[Service<IRecipientDirectory>(Multiple = true)]
public sealed class ManagedRecipientDirectory(IAuthorizationManagementStore store) : IRecipientDirectory
{
    /// <inheritdoc />
    public Task<RecipientContact?> FindUserAsync(string userId, CancellationToken ct = default) => Task.FromResult<RecipientContact?>(null);

    /// <inheritdoc />
    public async IAsyncEnumerable<string> UsersInRoleAsync(string role, string? tenantId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(role);
        if (RoleActivity.Deactivated(await store.GetRolesAsync(tenantId, ct).ConfigureAwait(false), tenantId).Contains(role))
            yield break;

        var now = DateTimeOffset.UtcNow;
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in await store.GetRoleHoldersAsync(role, ct).ConfigureAwait(false))
        {
            if (assignment.IsInForceAt(now) && InScope(assignment.TenantId, tenantId) && named.Add(assignment.UserId))
                yield return assignment.UserId;
        }

        foreach (var given in await store.GetRoleGroupsAsync(role, ct).ConfigureAwait(false))
        {
            if (!given.IsInForceAt(now) || !InScope(given.TenantId, tenantId))
                continue;
            foreach (var membership in await store.GetGroupMembersAsync(given.GroupName, ct).ConfigureAwait(false))
            {
                if (membership.IsInForceAt(now) && InScope(membership.TenantId, tenantId) && named.Add(membership.UserId))
                    yield return membership.UserId;
            }
        }
    }

    /// <summary>A row of <paramref name="rowTenant" /> counts in <paramref name="asked" />: the same tenant, or global.</summary>
    private static bool InScope(string? rowTenant, string? asked) => rowTenant is null || string.Equals(rowTenant, asked, StringComparison.Ordinal);
}
