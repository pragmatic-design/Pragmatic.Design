using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Groups;

/// <summary>Creates a new dynamic group (#726): a name users are made members of and roles are given to.</summary>
/// <remarks>Managed with the roles' permission: a group's roles are what it grants, as a role's permissions are.</remarks>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Roles.Manage)]
public partial class CreateGroup : DomainAction<Guid, ConflictError>
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    public required string Name { get; init; }
    public string? Description { get; init; }
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
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, tenantId, "create-group") is { } tenantError)
            return Result<Guid, IError>.Failure(tenantError);

        var groups = await _store.GetGroupsAsync(tenantId, ct).ConfigureAwait(false);
        if (groups.Any(g => g.Name == Name && g.TenantId == tenantId))
            return ConflictError.AlreadyExists("DynamicGroup", Name);

        var group = new DynamicGroup
        {
            Name = Name,
            Description = Description,
            TenantId = tenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = _currentUser.Id
        };

        await _store.AddGroupAsync(group, ct).ConfigureAwait(false);
        return group.PersistenceId;
    }
}
