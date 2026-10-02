using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Permissions;

/// <summary>Creates a new dynamic permission.</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Permissions.Manage)]
public partial class CreatePermission : DomainAction<Guid, ConflictError>
{
    private DbContext _dbContext = null!;
    private ICurrentUser _currentUser = null!;

    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? TenantId { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "create-permission") is { } tenantError)
            return Result<Guid, IError>.Failure(tenantError);

        var exists = await _dbContext.Set<DynamicPermission>()
            .AnyAsync(p => p.Name == Name && (p.TenantId == TenantId), ct)
            .ConfigureAwait(false);

        if (exists)
            return ConflictError.AlreadyExists("DynamicPermission", Name);

        var permission = new DynamicPermission
        {
            Name = Name,
            Description = Description,
            Category = Category,
            TenantId = TenantId
        };

        _dbContext.Set<DynamicPermission>().Add(permission);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return permission.PersistenceId;
    }
}
