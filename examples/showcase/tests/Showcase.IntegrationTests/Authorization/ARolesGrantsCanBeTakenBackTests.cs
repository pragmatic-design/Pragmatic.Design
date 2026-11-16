using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization.Management.Actions.Assignments;
using Pragmatic.Authorization.Management.Actions.Roles;
using Pragmatic.Identity;
using Pragmatic.Result;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     What a role grants can be taken back without deleting anything (#725): a revoked permission, and a deactivated role, are gone from
///     the role's holders on the next request, over HTTP and in <c>GetEffectivePermissions</c>, and a reactivated role gives back what it
///     granted.
/// </summary>
/// <remarks>
///     Before #725 the package could only add: <c>AssignPermissionsToRole</c> had no inverse, and a role could be silenced only by revoking
///     every assignment of it. <c>GetEffectivePermissions</c> also read the role permissions of the host's static store alone, so it never
///     listed what the package granted.
///     <para>
///         The roles here are global, managed by a platform operator: the serializer removes <c>TenantId</c> from every body
///         (<c>ReservedWireNames</c>), and a caller bound to no tenant acts globally over HTTP (#778). A global role's grants
///         reach its holders in the test tenant, where the probe is asked.
///     </para>
/// </remarks>
public class ARolesGrantsCanBeTakenBackTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Probe = "/api/reservations/confirmed";
    private const string ProbePermission = "booking.reservation.read";

    [Fact]
    public async Task AGrantedPermission_IsInTheEffectivePermissions()
    {
        var (userId, _) = await AHolderOfARoleGrantingTheProbeAsync();

        (await EffectivePermissionsAsync(userId)).Should().Contain(ProbePermission);
    }

    [Fact]
    public async Task ARevokedPermission_IsGoneFromItsHolders()
    {
        var (userId, role) = await AHolderOfARoleGrantingTheProbeAsync();
        var user = CreateClientAsWithPermissions(userId, "Managed User");
        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK);

        await SucceedsAsync(await OperatorPostsAsync("roles/revoke-permissions", new { roleName = role, permissions = new[] { ProbePermission } }));

        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the role no longer grants it");
        (await EffectivePermissionsAsync(userId)).Should().NotContain(ProbePermission);
    }

    /// <summary>A permission the role does not hold is not found: a revocation that changed nothing says so.</summary>
    [Fact]
    public async Task RevokingAPermissionTheRoleDoesNotHold_IsNotFound()
    {
        var (_, role) = await AHolderOfARoleGrantingTheProbeAsync();

        var response = await OperatorPostsAsync("roles/revoke-permissions", new { roleName = role, permissions = new[] { "catalog.amenity.read" } });

        await IsTheActionsNotFoundAsync(response);
    }

    [Fact]
    public async Task ADeactivatedRole_GrantsNothing_AndAReactivatedOneGivesItBack()
    {
        var (userId, role) = await AHolderOfARoleGrantingTheProbeAsync();
        var user = CreateClientAsWithPermissions(userId, "Managed User");

        await SucceedsAsync(await OperatorPostsAsync("roles/deactivate", new { roleName = role }));
        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a deactivated role grants nothing");
        (await EffectivePermissionsAsync(userId)).Should().NotContain(ProbePermission);

        await SucceedsAsync(await OperatorPostsAsync("roles/reactivate", new { roleName = role }));
        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK, "the grant and the assignment were kept");
        (await EffectivePermissionsAsync(userId)).Should().Contain(ProbePermission);
    }

    /// <summary>The control: deactivating another role takes nothing from this one's holders.</summary>
    [Fact]
    public async Task DeactivatingAnotherRole_LeavesTheHolderAlone()
    {
        var (userId, _) = await AHolderOfARoleGrantingTheProbeAsync();
        var other = NewId("night-desk");
        (await InvokeAsync<CreateRole, Guid>(new CreateRole { Name = other, TenantId = null })).IsSuccess.Should().BeTrue();

        await SucceedsAsync(await OperatorPostsAsync("roles/deactivate", new { roleName = other }));

        (await CreateClientAsWithPermissions(userId, "Managed User").GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeactivatingARoleNobodyCreated_IsNotFound()
    {
        var response = await OperatorPostsAsync("roles/deactivate", new { roleName = NewId("ghost") });

        await IsTheActionsNotFoundAsync(response);
    }

    /// <summary>The routes ask for the permission to manage roles: an operator without it is forbidden.</summary>
    [Fact]
    public async Task AnOperatorWithoutTheRolesPermission_IsForbidden()
    {
        var (_, role) = await AHolderOfARoleGrantingTheProbeAsync();

        var response = await CreateClientAsWithPermissions(NewId("operator"), "Operator", "authorization.view")
            .PostAsJsonAsync("/api/operations/roles/deactivate", new { roleName = role });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)];

    // The routes the Showcase exposes for the package's actions (AccountsModule), called by an operator who may manage roles.
    private Task<HttpResponseMessage> OperatorPostsAsync(string route, object body)
        => CreateClientAsWithPermissions(NewId("operator"), "Operator", "authorization.roles.manage")
            .PostAsJsonAsync($"/api/operations/{route}", body);

    private static async Task SucceedsAsync(HttpResponseMessage response)
        => response.IsSuccessStatusCode.Should().BeTrue(
            $"the action answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    // The action's NotFoundError, a ProblemDetails, and not a route nobody mapped: an empty 404 would pass for one.
    private static async Task IsTheActionsNotFoundAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        body.Should().Contain("DynamicRole");
    }

    // A role created in the tenant, granting the probe's permission, held by a new user.
    private async Task<(string UserId, string Role)> AHolderOfARoleGrantingTheProbeAsync()
    {
        var (userId, role) = (NewId("managed-user"), NewId("front-desk"));
        (await InvokeAsync<CreateRole, Guid>(new CreateRole { Name = role, TenantId = null })).IsSuccess.Should().BeTrue();
        (await InvokeAsync(new AssignPermissionsToRole { RoleName = role, Permissions = [ProbePermission], TenantId = null }))
            .IsSuccess.Should().BeTrue();
        (await InvokeAsync<AssignRoleToUser, Guid>(new AssignRoleToUser { UserId = userId, RoleName = role, TenantId = null })).IsSuccess.Should().BeTrue();
        return (userId, role);
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

    // A platform administrator (no tenant) who holds what the roles of these tests carry: an assigner who does not is refused.
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
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "RoleGrantsTest")),
        };
        return scope;
    }
}
