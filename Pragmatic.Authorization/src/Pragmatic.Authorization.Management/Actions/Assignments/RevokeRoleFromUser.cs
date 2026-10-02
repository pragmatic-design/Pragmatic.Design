using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Evaluation;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Assignments;

/// <summary>Revokes a role from a user by setting ValidTo to now.</summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.Assignments.Manage)]
public partial class RevokeRoleFromUser : VoidDomainAction<NotFoundError>
{
    private DbContext _dbContext = null!;
    private ICurrentUser _currentUser = null!;

    // Optional: registered only with a cross-request permission cache, and without one there is
    // nothing cached to evict — the next request resolves from the tables.
    private IPermissionCacheInvalidator? _permissionCache;

    public required string UserId { get; init; }
    public required string RoleName { get; init; }
    public string? TenantId { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "revoke-role") is { } tenantError)
            return VoidResult<IError>.Failure(tenantError);

        var now = DateTimeOffset.UtcNow;

        var assignment = await _dbContext.Set<UserRoleAssignment>()
            .FirstOrDefaultAsync(a =>
                a.UserId == UserId
                && a.RoleName == RoleName
                && (a.TenantId == TenantId)
                && a.ValidFrom <= now
                && (a.ValidTo == null || a.ValidTo > now), ct)
            .ConfigureAwait(false);

        if (assignment is null)
            return VoidResult<IError>.Failure(
                NotFoundError.Create("UserRoleAssignment", $"{UserId}/{RoleName}"));

        assignment.ValidTo = now;
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        // A revocation that waits for the cache to expire is a revocation that has not happened yet.
        if (_permissionCache is not null)
            await _permissionCache.InvalidateUserAsync(UserId, ct).ConfigureAwait(false);

        return VoidResult<IError>.Success();
    }
}
