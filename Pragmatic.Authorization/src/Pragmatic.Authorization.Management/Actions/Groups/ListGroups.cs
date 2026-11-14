using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.Result;

namespace Pragmatic.Authorization.Management.Actions.Groups;

/// <summary>
///     Lists the groups created with <see cref="CreateGroup" /> (#726), global and of the tenant asked, each with the roles given to it that are
///     in force.
/// </summary>
/// <remarks>The groups a host declares in code (<c>MapGroup</c>) are not listed: their roles are the host's, not rows of this package.</remarks>
[DomainAction]
[RequirePermission(AuthorizationPermissions.View)]
public partial class ListGroups : DomainAction<IReadOnlyList<GroupInfo>>
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    public string? TenantId { get; init; }

    public override async Task<Result<IReadOnlyList<GroupInfo>, IError>> Execute(CancellationToken ct = default)
    {
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "list-groups") is { } tenantError)
            return Result<IReadOnlyList<GroupInfo>, IError>.Failure(tenantError);

        var now = DateTimeOffset.UtcNow;
        var groups = await _store.GetGroupsAsync(TenantId, ct).ConfigureAwait(false);
        var roles = groups.Count == 0
            ? []
            : (await _store.GetGroupRolesAsync([.. groups.Select(g => g.Name)], TenantId, ct).ConfigureAwait(false))
                .Where(gr => gr.IsInForceAt(now))
                .ToList();

        IReadOnlyList<GroupInfo> listed =
        [
            .. groups.OrderBy(g => g.Name, StringComparer.Ordinal).Select(g => new GroupInfo(
                g.Name,
                g.Description,
                g.TenantId,
                [.. roles.Where(gr => string.Equals(gr.GroupName, g.Name, StringComparison.OrdinalIgnoreCase)).Select(gr => gr.RoleName)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)])),
        ];
        return Result<IReadOnlyList<GroupInfo>, IError>.Success(listed);
    }
}
