using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Roles;

/// <summary>Creates a new dynamic role.</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Roles.Manage)]
public partial class CreateRole : DomainAction<Guid, ConflictError>
{
    private DbContext _dbContext = null!;
    private ICurrentUser _currentUser = null!;

    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? TenantId { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "create-role") is { } tenantError)
            return Result<Guid, IError>.Failure(tenantError);

        var exists = await _dbContext.Set<DynamicRole>()
            .AnyAsync(r => r.Name == Name && r.TenantId == TenantId, ct)
            .ConfigureAwait(false);

        if (exists)
            return ConflictError.AlreadyExists("DynamicRole", Name);

        var role = new DynamicRole
        {
            Name = Name,
            Description = Description,
            TenantId = TenantId
        };

        _dbContext.Set<DynamicRole>().Add(role);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return role.PersistenceId;
    }
}
