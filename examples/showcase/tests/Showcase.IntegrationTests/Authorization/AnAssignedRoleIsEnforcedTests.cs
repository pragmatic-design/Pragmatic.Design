using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
///     A role assigned through <c>Pragmatic.Authorization.Management</c> grants what the role holds, on
///     the next request — and a revoked one stops granting it.
/// </summary>
/// <remarks>
///     The package wrote <c>UserRoleAssignment</c> and <c>DynamicRolePermission</c> and nothing in the
///     request pipeline read them: an administrator who assigned a role believed they had changed someone's
///     access, and had changed a table. Every test of the package checked the row, none the route.
/// </remarks>
public class AnAssignedRoleIsEnforcedTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Tenant = "test-tenant";
    private const string Probe = "/api/reservations/confirmed";
    private const string ProbePermission = "booking.reservation.read";

    [Fact]
    public async Task AnAssignedRole_GrantsItsPermissions()
    {
        var (userId, role) = (NewId("managed-user"), NewId("front-desk"));
        await GrantAsync(role, ProbePermission);
        await AssignAsync(userId, role);

        var user = CreateClientAsWithPermissions(userId, "Managed User");
        var response = await user.GetAsync(Probe);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the user holds {role}, which holds {ProbePermission}");
    }

    /// <summary>The control: the same user, before anything is assigned, is refused.</summary>
    [Fact]
    public async Task WithoutTheRole_TheRouteIsRefused()
    {
        var user = CreateClientAsWithPermissions(NewId("managed-user"), "Managed User");

        var response = await user.GetAsync(Probe);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARevokedRole_StopsGranting()
    {
        var (userId, role) = (NewId("managed-user"), NewId("front-desk"));
        await GrantAsync(role, ProbePermission);
        await AssignAsync(userId, role);

        var user = CreateClientAsWithPermissions(userId, "Managed User");
        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK);

        await RevokeAsync(userId, role);

        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a revocation takes the permission away on the next request, cached or not");
    }

    /// <summary>
    ///     A tenant administrator cannot put into a role a permission they do not hold: with the role and
    ///     assignment permissions together they would otherwise grant themselves anything.
    /// </summary>
    [Fact]
    public async Task APermissionTheGrantorDoesNotHold_IsRefused()
    {
        var result = await TryGrantAsync(
            Tenant, holding: ["authorization.roles.manage"], NewId("front-desk"), ProbePermission);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ForbiddenError>();
    }

    /// <summary>A wildcard the grantor holds covers what it names.</summary>
    [Fact]
    public async Task APermissionCoveredByTheGrantorsWildcard_IsGranted()
    {
        var result = await TryGrantAsync(
            Tenant, holding: ["authorization.roles.manage", "booking.*"], NewId("front-desk"), ProbePermission);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>An operator with no tenant is the platform, and is not bound by what they hold.</summary>
    [Fact]
    public async Task AnOperatorWithNoTenant_GrantsFreely()
    {
        var result = await TryGrantAsync(
            tenant: null, holding: ["authorization.roles.manage"], NewId("front-desk"), ProbePermission);

        result.IsSuccess.Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)];

    private async Task GrantAsync(string role, params string[] permissions)
    {
        var result = await TryGrantAsync(Tenant, ["authorization.roles.manage", .. permissions], role, permissions);
        result.IsSuccess.Should().BeTrue();
    }

    private async Task<VoidResult<IError>> TryGrantAsync(
        string? tenant, string[] holding, string role, params string[] permissions)
    {
        using var scope = AsAdministrator(tenant, holding);
        return await scope.ServiceProvider
            .GetRequiredService<IVoidDomainActionInvoker<AssignPermissionsToRole>>()
            .InvokeAsync(new AssignPermissionsToRole { RoleName = role, Permissions = permissions, TenantId = tenant });
    }

    private async Task AssignAsync(string userId, string role)
    {
        using var scope = AsAdministrator("authorization.assignments.manage");
        var result = await scope.ServiceProvider
            .GetRequiredService<IDomainActionInvoker<AssignRoleToUser, Guid>>()
            .InvokeAsync(new AssignRoleToUser { UserId = userId, RoleName = role, TenantId = Tenant });
        result.IsSuccess.Should().BeTrue();
    }

    private async Task RevokeAsync(string userId, string role)
    {
        using var scope = AsAdministrator("authorization.assignments.manage");
        var result = await scope.ServiceProvider
            .GetRequiredService<IVoidDomainActionInvoker<RevokeRoleFromUser>>()
            .InvokeAsync(new RevokeRoleFromUser { UserId = userId, RoleName = role, TenantId = Tenant });
        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    ///     A scope with an administrator on it — of the test tenant unless another is named, of none when
    ///     null — so the package's actions are invoked and authorized as they would be over HTTP.
    /// </summary>
    private IServiceScope AsAdministrator(params string[] permissions) => AsAdministrator(Tenant, permissions);

    private IServiceScope AsAdministrator(string? tenant, string[] permissions)
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
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "AssignedRoleTest")),
        };
        return scope;
    }
}
