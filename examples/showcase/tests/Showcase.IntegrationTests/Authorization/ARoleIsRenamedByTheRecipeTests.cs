using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization.Management.Actions.Assignments;
using Pragmatic.Authorization.Management.Actions.Groups;
using Pragmatic.Authorization.Management.Actions.Roles;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     There is no <c>RenameRole</c> (#777): a role's name is also written in claims, <c>MapRole</c> and group mappings the package does not
///     own. The rename is the recipe the Management README gives, with the package's own actions, and this follows it: a holder moved to the
///     new role, directly or through a group, keeps what the old one granted, and the old one, deactivated, grants nothing.
/// </summary>
/// <remarks>
///     Global roles and a platform administrator, as in <see cref="ARolesGrantsCanBeTakenBackTests" />: an exposed management action
///     cannot name a tenant over HTTP (#778).
/// </remarks>
public class ARoleIsRenamedByTheRecipeTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Probe = "/api/reservations/confirmed";
    private const string ProbePermission = "booking.reservation.read";

    [Fact]
    public async Task The_recipe_moves_the_holders_to_the_new_role_and_the_old_one_grants_nothing()
    {
        var (oldRole, newRole) = (NewId("front-desk"), NewId("reception"));
        var (direct, member, leftBehind, group) = (NewId("direct"), NewId("member"), NewId("left-behind"), NewId("night-shift"));

        // The role as it is before the rename: granting the probe, held by a user directly, by a group's member, and by one the recipe skips.
        await SucceedsAsync(InvokeAsync<CreateRole, Guid>(new CreateRole { Name = oldRole, TenantId = null }));
        await SucceedsAsync(InvokeAsync(new AssignPermissionsToRole { RoleName = oldRole, Permissions = [ProbePermission], TenantId = null }));
        await SucceedsAsync(InvokeAsync<AssignRoleToUser, Guid>(new AssignRoleToUser { UserId = direct, RoleName = oldRole, TenantId = null }));
        await SucceedsAsync(InvokeAsync<AssignRoleToUser, Guid>(new AssignRoleToUser { UserId = leftBehind, RoleName = oldRole, TenantId = null }));
        await SucceedsAsync(InvokeAsync<CreateGroup, Guid>(new CreateGroup { Name = group, TenantId = null }));
        await SucceedsAsync(InvokeAsync<AssignRoleToGroup, Guid>(new AssignRoleToGroup { GroupName = group, RoleName = oldRole, TenantId = null }));
        await SucceedsAsync(InvokeAsync<AssignUserToGroup, Guid>(new AssignUserToGroup { UserId = member, GroupName = group, TenantId = null }));

        // The recipe: the new role, the old one's grants copied, the assignments moved (the user's and the group's), the old one deactivated.
        await SucceedsAsync(InvokeAsync<CreateRole, Guid>(new CreateRole { Name = newRole, TenantId = null }));
        await SucceedsAsync(InvokeAsync(new AssignPermissionsToRole { RoleName = newRole, Permissions = [ProbePermission], TenantId = null }));
        await SucceedsAsync(InvokeAsync<AssignRoleToUser, Guid>(new AssignRoleToUser { UserId = direct, RoleName = newRole, TenantId = null }));
        await SucceedsAsync(InvokeAsync(new RevokeRoleFromUser { UserId = direct, RoleName = oldRole, TenantId = null }));
        await SucceedsAsync(InvokeAsync<AssignRoleToGroup, Guid>(new AssignRoleToGroup { GroupName = group, RoleName = newRole, TenantId = null }));
        await SucceedsAsync(InvokeAsync(new RevokeRoleFromGroup { GroupName = group, RoleName = oldRole, TenantId = null }));
        await SucceedsAsync(InvokeAsync(new DeactivateRole { RoleName = oldRole, TenantId = null }));

        foreach (var holder in new[] { direct, member })
        {
            (await CreateClientAsWithPermissions(holder, "Renamed Holder").GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK,
                $"{holder} holds the new role, which grants what the old one did");
            (await EffectivePermissionsAsync(holder)).Should().Contain(ProbePermission);
        }

        // The control: a holder the recipe did not move keeps only the old role, which grants nothing once deactivated.
        (await CreateClientAsWithPermissions(leftBehind, "Left Behind").GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the old role is deactivated");
        (await EffectivePermissionsAsync(leftBehind)).Should().NotContain(ProbePermission);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)];

    private static async Task SucceedsAsync(Task<VoidResult<IError>> invocation)
    {
        var result = await invocation;
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error?.ToString() : "");
    }

    private static async Task SucceedsAsync<T>(Task<Result<T, IError>> invocation)
    {
        var result = await invocation;
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error?.ToString() : "");
    }

    private async Task<IReadOnlyList<string>> EffectivePermissionsAsync(string userId)
    {
        var result = await InvokeAsync<GetEffectivePermissions, IReadOnlyList<string>>(new GetEffectivePermissions { UserId = userId, TenantId = null });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<VoidResult<IError>> InvokeAsync<TAction>(TAction action) where TAction : VoidDomainAction
    {
        using var scope = AsAdministrator();
        return await scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<TAction>>().InvokeAsync(action);
    }

    private async Task<Result<TReturn, IError>> InvokeAsync<TAction, TReturn>(TAction action) where TAction : DomainAction<TReturn>
    {
        using var scope = AsAdministrator();
        return await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<TAction, TReturn>>().InvokeAsync(action);
    }

    // A platform administrator (no tenant) who holds what the roles carry: an assigner who does not is refused.
    private IServiceScope AsAdministrator()
    {
        var scope = Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Claim[] claims =
        [
            new(options.UserIdClaimType, $"admin-{Guid.NewGuid():N}"),
            new(options.PermissionClaimType, "authorization.roles.manage"),
            new(options.PermissionClaimType, "authorization.assignments.manage"),
            new(options.PermissionClaimType, "authorization.view"),
            new(options.PermissionClaimType, ProbePermission),
        ];
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "RoleRenameRecipeTest")),
        };
        return scope;
    }
}
