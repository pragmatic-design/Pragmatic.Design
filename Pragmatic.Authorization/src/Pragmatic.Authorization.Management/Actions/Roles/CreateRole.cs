using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Roles;

/// <summary>Creates a new dynamic role.</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Roles.Manage)]
public partial class CreateRole : DomainAction<Guid, ConflictError>
{
    private IAuthorizationManagementStore _store = null!;
    private ICurrentUser _currentUser = null!;

    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? TenantId { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "create-role") is { } tenantError)
            return Result<Guid, IError>.Failure(tenantError);

        var roles = await _store.GetRolesAsync(TenantId, ct).ConfigureAwait(false);
        if (roles.Any(r => r.Name == Name && r.TenantId == TenantId))
            return ConflictError.AlreadyExists("DynamicRole", Name);

        var role = new DynamicRole
        {
            Name = Name,
            Description = Description,
            TenantId = TenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = _currentUser.Id
        };

        await _store.AddRoleAsync(role, ct).ConfigureAwait(false);
        return role.PersistenceId;
    }
}
