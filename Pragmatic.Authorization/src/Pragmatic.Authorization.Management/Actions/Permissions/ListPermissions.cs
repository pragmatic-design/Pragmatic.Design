using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Catalog;
using Pragmatic.Result;

namespace Pragmatic.Authorization.Management.Actions.Permissions;

/// <summary>Lists all known permissions (static + dynamic).</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.View)]
public partial class ListPermissions : DomainAction<IReadOnlyList<PermissionInfo>>
{
    private IPermissionCatalog _catalog = null!;

    public override async Task<Result<IReadOnlyList<PermissionInfo>, IError>> Execute(CancellationToken ct = default)
    {
        var permissions = await _catalog.GetAllPermissionsAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<PermissionInfo>, IError>.Success(permissions);
    }
}
