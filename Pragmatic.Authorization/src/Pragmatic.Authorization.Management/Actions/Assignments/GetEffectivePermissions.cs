using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Stores;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Authorization.Management.Actions.Assignments;

/// <summary>
///     Gets the effective permissions for a user by resolving all active role and group assignments
///     (group → role → permission, mirroring what GroupExpansionProvider grants at runtime).
/// </summary>
[DomainAction]
[RequirePermission(AuthorizationPermissions.View)]
public partial class GetEffectivePermissions : DomainAction<IReadOnlyList<string>>
{
    private DbContext _dbContext = null!;
    private IRolePermissionStore _roleStore = null!;
    private ICurrentUser _currentUser = null!;

    // NOT named _serviceProvider: the Actions SG turns each injected field into an invoker ctor
    // parameter (camelCased), and the invoker already has its own `serviceProvider` parameter —
    // a field named _serviceProvider produces a duplicate parameter (CS0100). Resolved by type, so
    // the name is free. Used for the OPTIONAL IGroupRoleStore (the SG has no optional field injection).
    private IServiceProvider _rootServices = null!;

    public required string UserId { get; init; }
    public string? TenantId { get; init; }

    public override async Task<Result<IReadOnlyList<string>, IError>> Execute(CancellationToken ct = default)
    {
        if (TenantScopeGuard.CheckTenantBinding(_currentUser, TenantId, "get-effective-permissions") is { } tenantError)
            return Result<IReadOnlyList<string>, IError>.Failure(tenantError);

        var now = DateTimeOffset.UtcNow;

        // Get active role assignments for the user
        var activeRoles = await _dbContext.Set<UserRoleAssignment>()
            .Where(a => a.UserId == UserId
                        && (a.TenantId == null || a.TenantId == TenantId)
                        && a.ValidFrom <= now
                        && (a.ValidTo == null || a.ValidTo > now))
            .Select(a => a.RoleName)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var roleNames = new HashSet<string>(activeRoles, StringComparer.OrdinalIgnoreCase);

        // Group assignments contribute their roles too — without this expansion the admin
        // answer omits everything a user gets via groups, which is incomplete and misleading.
        // IGroupRoleStore is optional (absent when no group store is configured), so it is
        // resolved lazily instead of being a hard ctor dependency of the invoker.
        if (_rootServices.GetService<IGroupRoleStore>() is { } groupStore)
        {
            var activeGroups = await _dbContext.Set<UserGroupAssignment>()
                .Where(a => a.UserId == UserId
                            && (a.TenantId == null || a.TenantId == TenantId)
                            && a.ValidFrom <= now
                            && (a.ValidTo == null || a.ValidTo > now))
                .Select(a => a.GroupName)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var group in activeGroups)
            {
                var groupRoles = await groupStore.GetRolesForGroupAsync(group, ct).ConfigureAwait(false);
                roleNames.UnionWith(groupRoles);
            }
        }

        // One store call per role, run in parallel: IRolePermissionStore reads a role at a time, so the
        // cost grows with the number of roles a user holds, not with a single round trip.
        var permissionSets = await Task.WhenAll(
            roleNames.Select(r => _roleStore.GetPermissionsForRoleAsync(r, ct).AsTask()))
            .ConfigureAwait(false);

        var allPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permSet in permissionSets)
            foreach (var permission in permSet)
                allPermissions.Add(permission);

        IReadOnlyList<string> result = allPermissions.Order().ToList();
        return Result<IReadOnlyList<string>, IError>.Success(result);
    }
}
