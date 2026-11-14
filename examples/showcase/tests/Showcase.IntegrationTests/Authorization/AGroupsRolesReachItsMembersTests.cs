using System.Net;
using System.Net.Http.Json;
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
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     A group's roles reach its members (#726): a user made a member of a group holding a role gets what the role grants on the next request,
///     over HTTP and in <c>GetEffectivePermissions</c>, and loses it when removed or when the group's role is taken back.
/// </summary>
/// <remarks>
///     Before #726 the package had a membership table nothing wrote, and no group could be given a role: groups could not be used.
///     <para>
///         The groups and roles here are global, managed by a platform operator, as in <see cref="ARolesGrantsCanBeTakenBackTests" />: the
///         serializer removes <c>TenantId</c> from every body (<c>ReservedWireNames</c>), so an exposed management action cannot name a tenant
///         over HTTP (#778). The rule for a caller bound to a tenant is invoked in process, where the tenant can be named.
///     </para>
/// </remarks>
public class AGroupsRolesReachItsMembersTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Probe = "/api/reservations/confirmed";
    private const string ProbePermission = "booking.reservation.read";
    private const string Tenant = "test-tenant";

    [Fact]
    public async Task AMemberOfAGroupHoldingARole_GetsItsPermissions_AndLosesThemWhenRemoved()
    {
        var group = await AGroupHoldingARoleGrantingTheProbeAsync();
        var userId = NewId("member");
        var user = CreateClientAsWithPermissions(userId, "Group Member");
        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the control: not a member yet");

        await SucceedsAsync(await OperatorPostsAsync("groups/add-member", new { userId, groupName = group }));

        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK, "a member holds the group's role");
        (await EffectivePermissionsAsync(userId)).Should().Contain(ProbePermission);

        await SucceedsAsync(await OperatorPostsAsync("groups/remove-member", new { userId, groupName = group }));

        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a removed member no longer holds it");
        (await EffectivePermissionsAsync(userId)).Should().NotContain(ProbePermission);
    }

    [Fact]
    public async Task ARoleTakenBackFromTheGroup_IsGoneFromItsMembers()
    {
        var (group, role) = (NewId("night-shift"), NewId("front-desk"));
        await ARoleGrantingTheProbeAsync(role);
        await SucceedsAsync(await OperatorPostsAsync("groups", new { name = group }));
        await SucceedsAsync(await OperatorPostsAsync("groups/assign-role", new { groupName = group, roleName = role }));
        var userId = NewId("member");
        await SucceedsAsync(await OperatorPostsAsync("groups/add-member", new { userId, groupName = group }));
        var user = CreateClientAsWithPermissions(userId, "Group Member");
        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK);

        await SucceedsAsync(await OperatorPostsAsync("groups/revoke-role", new { groupName = group, roleName = role }));

        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the group no longer holds the role");
        (await EffectivePermissionsAsync(userId)).Should().NotContain(ProbePermission);
    }

    /// <summary>The control: a member of a group that holds no role gets nothing from it.</summary>
    [Fact]
    public async Task AMemberOfAGroupWithoutRoles_GetsNothing()
    {
        var group = NewId("empty");
        await SucceedsAsync(await OperatorPostsAsync("groups", new { name = group }));
        var userId = NewId("member");
        await SucceedsAsync(await OperatorPostsAsync("groups/add-member", new { userId, groupName = group }));

        (await CreateClientAsWithPermissions(userId, "Group Member").GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>In process: over GET the action's optional init input does not compile in the generated handler (#780).</summary>
    [Fact]
    public async Task ListGroups_ShowsAGroupWithTheRolesGivenToIt()
    {
        var (group, role, revoked) = (NewId("night-shift"), NewId("front-desk"), NewId("old-desk"));
        await ARoleGrantingTheProbeAsync(role);
        await ARoleGrantingTheProbeAsync(revoked);
        await SucceedsAsync(await OperatorPostsAsync("groups", new { name = group, description = "Covers the night" }));
        await SucceedsAsync(await OperatorPostsAsync("groups/assign-role", new { groupName = group, roleName = role }));
        await SucceedsAsync(await OperatorPostsAsync("groups/assign-role", new { groupName = group, roleName = revoked }));
        await SucceedsAsync(await OperatorPostsAsync("groups/revoke-role", new { groupName = group, roleName = revoked }));

        Result<IReadOnlyList<GroupInfo>, IError> result;
        using (var scope = As(tenant: null, "authorization.view"))
            result = await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<ListGroups, IReadOnlyList<GroupInfo>>>()
                .InvokeAsync(new ListGroups { TenantId = null });

        result.IsSuccess.Should().BeTrue();
        var entry = result.Value.Single(g => g.Name == group);
        entry.Description.Should().Be("Covers the night");
        entry.Roles.Should().Equal([role], "a role taken back is not listed");
    }

    [Fact]
    public async Task ASecondGroupOfTheSameName_IsAConflict()
    {
        var group = NewId("night-shift");
        await SucceedsAsync(await OperatorPostsAsync("groups", new { name = group }));

        (await OperatorPostsAsync("groups", new { name = group })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RemovingSomebodyWhoIsNotAMember_IsNotFound()
    {
        var response = await OperatorPostsAsync("groups/remove-member", new { userId = NewId("stranger"), groupName = NewId("night-shift") });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        body.Should().Contain("UserGroupAssignment", "the action's own 404, not a route nobody mapped");
    }

    /// <summary>
    ///     A caller bound to a tenant may give through a group only what they hold, as through a direct assignment: the assignment
    ///     permission alone would otherwise put anyone, themselves included, in a group carrying any role of the tenant.
    /// </summary>
    [Fact]
    public async Task ATenantCallerWhoDoesNotHoldTheGroupsRole_CannotAddAMember()
    {
        var group = await AGroupHoldingARoleGrantingTheProbeAsync();

        Result<Guid, IError> result;
        using (var scope = As(Tenant, "authorization.assignments.manage"))
            result = await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<AssignUserToGroup, Guid>>()
                .InvokeAsync(new AssignUserToGroup { UserId = NewId("member"), GroupName = group, TenantId = Tenant });

        result.Error.Should().BeOfType<ForbiddenError>();
    }

    /// <summary>The control: one who holds the role's permission adds the member.</summary>
    [Fact]
    public async Task ATenantCallerWhoHoldsTheGroupsRole_AddsAMember()
    {
        var group = await AGroupHoldingARoleGrantingTheProbeAsync();

        Result<Guid, IError> result;
        using (var scope = As(Tenant, "authorization.assignments.manage", ProbePermission))
            result = await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<AssignUserToGroup, Guid>>()
                .InvokeAsync(new AssignUserToGroup { UserId = NewId("member"), GroupName = group, TenantId = Tenant });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error?.ToString() : "");
    }

    [Fact]
    public async Task ATenantCallerWhoDoesNotHoldTheRole_CannotGiveItToAGroup()
    {
        var role = NewId("front-desk");
        await ARoleGrantingTheProbeAsync(role);

        Result<Guid, IError> result;
        using (var scope = As(Tenant, "authorization.roles.manage"))
            result = await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<AssignRoleToGroup, Guid>>()
                .InvokeAsync(new AssignRoleToGroup { GroupName = NewId("night-shift"), RoleName = role, TenantId = Tenant });

        result.Error.Should().BeOfType<ForbiddenError>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)];

    // The routes the Showcase exposes for the package's actions (AccountsModule), called by a platform operator who may manage roles and
    // assignments, and who holds the probe's permission, so the grantor's rule does not stand in the way.
    private Task<HttpResponseMessage> OperatorPostsAsync(string route, object body)
        => CreateClientAsWithPermissions(NewId("operator"), "Operator", "authorization.roles.manage", "authorization.assignments.manage")
            .PostAsJsonAsync($"/api/operations/{route}", body);

    private static async Task SucceedsAsync(HttpResponseMessage response)
        => response.IsSuccessStatusCode.Should().BeTrue(
            $"the action answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    // A global role granting the probe's permission, created in process by a platform administrator.
    private async Task ARoleGrantingTheProbeAsync(string role)
    {
        using var scope = As(tenant: null, "authorization.roles.manage", ProbePermission);
        (await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<CreateRole, Guid>>()
            .InvokeAsync(new CreateRole { Name = role, TenantId = null })).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<AssignPermissionsToRole>>()
            .InvokeAsync(new AssignPermissionsToRole { RoleName = role, Permissions = [ProbePermission], TenantId = null })).IsSuccess.Should().BeTrue();
    }

    // A global group, given over HTTP a global role that grants the probe's permission.
    private async Task<string> AGroupHoldingARoleGrantingTheProbeAsync()
    {
        var (group, role) = (NewId("night-shift"), NewId("front-desk"));
        await ARoleGrantingTheProbeAsync(role);
        await SucceedsAsync(await OperatorPostsAsync("groups", new { name = group }));
        await SucceedsAsync(await OperatorPostsAsync("groups/assign-role", new { groupName = group, roleName = role }));
        return group;
    }

    private async Task<IReadOnlyList<string>> EffectivePermissionsAsync(string userId)
    {
        using var scope = As(tenant: null, "authorization.view");
        var result = await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<GetEffectivePermissions, IReadOnlyList<string>>>()
            .InvokeAsync(new GetEffectivePermissions { UserId = userId, TenantId = null });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    // A caller of the package's actions in process, in a tenant or none (the platform), holding the given permissions.
    private IServiceScope As(string? tenant, params string[] permissions)
    {
        var scope = Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var claims = new List<Claim> { new(options.UserIdClaimType, $"admin-{Guid.NewGuid():N}") };
        if (tenant is not null)
            claims.Add(new Claim(options.TenantClaimType, tenant));
        claims.AddRange(permissions.Select(p => new Claim(options.PermissionClaimType, p)));
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "GroupsTest")),
        };
        return scope;
    }
}
