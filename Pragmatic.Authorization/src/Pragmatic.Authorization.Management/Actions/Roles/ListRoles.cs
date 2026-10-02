using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Catalog;
using Pragmatic.Result;

namespace Pragmatic.Authorization.Management.Actions.Roles;

/// <summary>Lists all known roles (static + dynamic).</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.View)]
public partial class ListRoles : DomainAction<IReadOnlyList<RoleInfo>>
{
    private IPermissionCatalog _catalog = null!;

    public override async Task<Result<IReadOnlyList<RoleInfo>, IError>> Execute(CancellationToken ct = default)
    {
        var roles = await _catalog.GetAllRolesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<RoleInfo>, IError>.Success(roles);
    }
}
