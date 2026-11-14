using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization.Management.Actions.Roles;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     An anonymous caller holds the host's anonymous role (#722): an operation under <c>[AllowAnonymous]</c> and
///     <c>[RequirePermission]</c> answers it 200 once an administrator grants the role the permission, and 401 while the role lacks it.
/// </summary>
/// <remarks>
///     Before #722 the second answer was the only one: an anonymous caller was refused before its permissions were asked, so a public
///     operation could not be gated by a grant an administrator can withdraw. Each test grants in a tenant of its own, so a grant made by
///     one cannot open the route for another.
/// </remarks>
public class AnAnonymousCallerHoldsTheAnonymousRoleTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Route = "/api/amenities/public-notice";
    private const string Permission = "catalog.amenity.read";

    [Fact]
    public async Task WhenTheAnonymousRoleHoldsThePermission_AnAnonymousCallerIsServed()
    {
        var tenant = NewTenant();
        await GrantTheAnonymousRoleAsync(tenant, Permission);

        var response = await AnonymousIn(tenant).GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the anonymous role holds the permission");
    }

    /// <summary>The control: the same route, in a tenant whose anonymous role was granted nothing.</summary>
    [Fact]
    public async Task WhenTheAnonymousRoleLacksThePermission_AnAnonymousCallerIsAskedToAuthenticate()
    {
        var response = await AnonymousIn(NewTenant()).GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A grant of another permission opens nothing: the role is asked for the one the operation requires.</summary>
    [Fact]
    public async Task WhenTheAnonymousRoleHoldsAnotherPermission_AnAnonymousCallerIsAskedToAuthenticate()
    {
        var tenant = NewTenant();
        await GrantTheAnonymousRoleAsync(tenant, "catalog.property.read");

        var response = await AnonymousIn(tenant).GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string NewTenant() => $"anon-{Guid.NewGuid():N}"[..17];

    private HttpClient AnonymousIn(string tenant)
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);
        return client;
    }

    private async Task GrantTheAnonymousRoleAsync(string tenant, string permission)
    {
        using var scope = Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Claim[] claims =
        [
            new(options.UserIdClaimType, $"admin-{Guid.NewGuid():N}"),
            new(options.TenantClaimType, tenant),
            new(options.PermissionClaimType, "authorization.roles.manage"),
            new(options.PermissionClaimType, permission),
        ];
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "AnonymousRoleTest")),
        };

        var result = await scope.ServiceProvider
            .GetRequiredService<IVoidDomainActionInvoker<AssignPermissionsToRole>>()
            .InvokeAsync(new AssignPermissionsToRole { RoleName = "anonymous", Permissions = [permission], TenantId = tenant });
        result.IsSuccess.Should().BeTrue();
    }
}
