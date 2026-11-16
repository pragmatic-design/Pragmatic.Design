using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Catalog;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Permissions;

/// <summary>Creates a new dynamic permission.</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Permissions.Manage)]
public partial class CreatePermission : DomainAction<Guid, ConflictError>
{
    private IAuthorizationManagementStore _store = null!;
    private IPermissionCatalog _catalog = null!;
    private ICurrentUser _currentUser = null!;

    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? TenantId { get; init; }

    /// <summary>
    ///     The request's tenant, written by the invoker: over HTTP a body never carries <see cref="TenantId" />, so this is the scope for a
    ///     caller bound to a tenant (#778).
    /// </summary>
    [FromTenant]
    public string? RequestTenantId { get; private set; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var tenantId = TenantScopeGuard.Scope(_currentUser, RequestTenantId, TenantId);
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, tenantId, "create-permission") is { } tenantError)
            return Result<Guid, IError>.Failure(tenantError);

        // A name the catalog already holds — a generated permission, a platform one, or a dynamic one of this tenant or global — is taken:
        // a dynamic twin of it would be a permission a tenant's administrator controls under a name everything else trusts.
        var catalog = await _catalog.GetAllPermissionsAsync(ct).ConfigureAwait(false);
        if (catalog.Any(p => string.Equals(p.Name, Name, StringComparison.OrdinalIgnoreCase)))
            return ConflictError.AlreadyExists("Permission", Name);

        var permissions = await _store.GetPermissionsAsync(tenantId, ct).ConfigureAwait(false);
        if (permissions.Any(p => p.Name == Name && p.TenantId == tenantId))
            return ConflictError.AlreadyExists("DynamicPermission", Name);

        var permission = new DynamicPermission
        {
            Name = Name,
            Description = Description,
            Category = Category,
            TenantId = tenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = _currentUser.Id
        };

        await _store.AddPermissionAsync(permission, ct).ConfigureAwait(false);
        return permission.PersistenceId;
    }
}
