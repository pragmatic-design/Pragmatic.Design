using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Assignments;

/// <summary>Assigns a role to a user with optional temporal validity.</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Assignments.Manage)]
public partial class AssignRoleToUser : DomainAction<Guid>
{
    private DbContext _dbContext = null!;
    private ICurrentUser _currentUser = null!;

    // Optional: registered only with a cross-request permission cache, and without one there is
    // nothing cached to evict — the next request resolves from the tables.
    private IPermissionCacheInvalidator? _permissionCache;

    public required string UserId { get; init; }
    public required string RoleName { get; init; }
    public string? TenantId { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Tenant binding enforced uniformly across all management actions — see TenantScopeGuard.
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "assign-role") is { } tenantError)
            return Result<Guid, IError>.Failure(tenantError);

        // Guard: prevent duplicate active assignment for the same user/role/tenant
        var alreadyAssigned = await _dbContext.Set<UserRoleAssignment>()
            .AnyAsync(a =>
                a.UserId == UserId
                && a.RoleName == RoleName
                && a.TenantId == TenantId
                && a.ValidFrom <= now
                && (a.ValidTo == null || a.ValidTo > now), ct)
            .ConfigureAwait(false);

        if (alreadyAssigned)
            return ConflictError.AlreadyExists("UserRoleAssignment", $"{UserId}/{RoleName}");

        var assignment = new UserRoleAssignment
        {
            UserId = UserId,
            RoleName = RoleName,
            TenantId = TenantId,
            AssignedBy = _currentUser.Id,
            ValidFrom = ValidFrom ?? now,
            ValidTo = ValidTo
        };

        _dbContext.Set<UserRoleAssignment>().Add(assignment);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        if (_permissionCache is not null)
            await _permissionCache.InvalidateUserAsync(UserId, ct).ConfigureAwait(false);

        return Result<Guid, IError>.Success(assignment.PersistenceId);
    }
}
