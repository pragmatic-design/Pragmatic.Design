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
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     An exposed management action called over HTTP by a tenant administrator acts on the request's tenant (#778): the serializer removes
///     <c>TenantId</c> from every body (<c>ReservedWireNames</c>), so the scope comes from the resolved tenant, not from the body.
/// </summary>
/// <remarks>
///     Before #778 the body's tenant never reached the action, which then read "global": a tenant administrator was refused, and a platform
///     operator acted globally whatever the body said. A caller with no tenant still acts globally, as
///     <see cref="ARolesGrantsCanBeTakenBackTests" /> does over the same routes.
/// </remarks>
public class ATenantAdministratorActsOnItsTenantTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Probe = "/api/reservations/confirmed";
    private const string ProbePermission = "booking.reservation.read";
    private const string Tenant = "test-tenant";

    [Fact]
    public async Task ATenantAdministrator_DeactivatesARoleOfItsTenant_AndItStopsGrantingThere()
    {
        var (holder, role) = await AHolderOfATenantRoleGrantingTheProbeAsync();
        var user = CreateClientAsWithPermissions(holder, "Tenant Holder");
        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK, "the control: the tenant role grants the probe");

        var response = await AdministratorOf(Tenant).PostAsJsonAsync("/api/operations/roles/deactivate", new { roleName = role });

        response.IsSuccessStatusCode.Should().BeTrue(
            $"the action answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        (await user.GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the role was deactivated in the tenant");
        (await EffectivePermissionsAsync(holder)).Should().NotContain(ProbePermission);
    }

    /// <summary>An administrator of another tenant, in a request resolved to this one, is refused: the request does not lend a tenant.</summary>
    [Fact]
    public async Task AnAdministratorOfAnotherTenant_IsForbidden()
    {
        var (holder, role) = await AHolderOfATenantRoleGrantingTheProbeAsync();

        var response = await AdministratorOf("another-tenant").PostAsJsonAsync("/api/operations/roles/deactivate", new { roleName = role });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CreateClientAsWithPermissions(holder, "Tenant Holder").GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    ///     A tenant administrator cannot reach the global scope: a global role of the same kind is looked for in the tenant, not found, and
    ///     keeps granting.
    /// </summary>
    [Fact]
    public async Task ATenantAdministrator_CannotDeactivateAGlobalRole()
    {
        var (holder, role) = await AHolderOfARoleGrantingTheProbeAsync(tenant: null);

        var response = await AdministratorOf(Tenant).PostAsJsonAsync("/api/operations/roles/deactivate", new { roleName = role });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await CreateClientAsWithPermissions(holder, "Global Holder").GetAsync(Probe)).StatusCode.Should().Be(HttpStatusCode.OK,
            "the global role was not touched");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)];

    // A caller bound to a tenant (its own tenant claim), in a request resolved to the test tenant, who may manage roles.
    private HttpClient AdministratorOf(string tenant)
    {
        var client = CreateClientAsWithPermissions(NewId("tenant-admin"), "Tenant Admin", "authorization.roles.manage", ProbePermission);
        client.DefaultRequestHeaders.Add("X-User-Tenant", tenant);
        return client;
    }

    private Task<(string Holder, string Role)> AHolderOfATenantRoleGrantingTheProbeAsync()
        => AHolderOfARoleGrantingTheProbeAsync(Tenant);

    // A role created by a platform administrator in the given scope, granting the probe's permission, held there by a new user.
    private async Task<(string Holder, string Role)> AHolderOfARoleGrantingTheProbeAsync(string? tenant)
    {
        var (holder, role) = (NewId("holder"), NewId("front-desk"));
        (await InvokeAsync<CreateRole, Guid>(new CreateRole { Name = role, TenantId = tenant })).IsSuccess.Should().BeTrue();
        (await InvokeAsync(new AssignPermissionsToRole { RoleName = role, Permissions = [ProbePermission], TenantId = tenant }))
            .IsSuccess.Should().BeTrue();
        (await InvokeAsync<AssignRoleToUser, Guid>(new AssignRoleToUser { UserId = holder, RoleName = role, TenantId = tenant }))
            .IsSuccess.Should().BeTrue();
        return (holder, role);
    }

    private async Task<IReadOnlyList<string>> EffectivePermissionsAsync(string userId)
    {
        var result = await InvokeAsync<GetEffectivePermissions, IReadOnlyList<string>>(new GetEffectivePermissions { UserId = userId, TenantId = Tenant });
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
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TenantAdministratorTest")),
        };
        return scope;
    }
}
