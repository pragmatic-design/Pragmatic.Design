using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization;
using Pragmatic.Authorization.Management.Actions.Permissions;
using Pragmatic.Authorization.Management.Actions.Roles;
using Pragmatic.Identity;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     A role or permission created through <c>Pragmatic.Authorization.Management</c> is listed by the
///     catalog — in its tenant, and not in another.
/// </summary>
/// <remarks>
///     The package shipped <c>EfDynamicRoleStore</c> and <c>EfDynamicPermissionStore</c> and nothing
///     registered them, so <c>ListRoles</c> and <c>ListPermissions</c> answered with the compiled catalog
///     alone: a role an administrator had just created was nowhere in the list they were given.
/// </remarks>
public class ARoleCreatedAtRuntimeIsCatalogedTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Tenant = "test-tenant";

    [Fact]
    public async Task ACreatedRole_IsListedWithItsPermissions()
    {
        var role = $"auditor-{Guid.NewGuid():N}"[..20];
        using (var scope = As(Tenant, "authorization.roles.manage", "booking.reservation.read"))
        {
            (await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<CreateRole, Guid>>()
                .InvokeAsync(new CreateRole { Name = role, TenantId = Tenant })).IsSuccess.Should().BeTrue();
            (await scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<AssignPermissionsToRole>>()
                .InvokeAsync(new AssignPermissionsToRole
                {
                    RoleName = role, Permissions = ["booking.reservation.read"], TenantId = Tenant,
                })).IsSuccess.Should().BeTrue();
        }

        var listed = await ListRolesAsAsync(Tenant);

        listed.Should().ContainSingle(r => r.Name == role)
            .Which.DefaultPermissions.Should().BeEquivalentTo(["booking.reservation.read"]);
    }

    [Fact]
    public async Task ACreatedPermission_IsListed()
    {
        var permission = $"custom.reports.{Guid.NewGuid():N}"[..24];
        using (var scope = As(Tenant, "authorization.permissions.manage"))
        {
            (await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<CreatePermission, Guid>>()
                .InvokeAsync(new CreatePermission { Name = permission, Category = "reports", TenantId = Tenant }))
                .IsSuccess.Should().BeTrue();
        }

        using var reader = As(Tenant, "authorization.view");
        var listed = await reader.ServiceProvider
            .GetRequiredService<IDomainActionInvoker<ListPermissions, IReadOnlyList<PermissionInfo>>>()
            .InvokeAsync(new ListPermissions());

        listed.IsSuccess.Should().BeTrue();
        listed.Value.Should().Contain(p => p.Name == permission);
    }

    /// <summary>The control: another tenant's catalog does not list the role.</summary>
    [Fact]
    public async Task ARoleOfOneTenant_IsNotListedInAnother()
    {
        var role = $"auditor-{Guid.NewGuid():N}"[..20];
        using (var scope = As(Tenant, "authorization.roles.manage"))
        {
            (await scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<CreateRole, Guid>>()
                .InvokeAsync(new CreateRole { Name = role, TenantId = Tenant })).IsSuccess.Should().BeTrue();
        }

        var listed = await ListRolesAsAsync("another-tenant");

        listed.Should().NotContain(r => r.Name == role);
    }

    private async Task<IReadOnlyList<RoleInfo>> ListRolesAsAsync(string tenant)
    {
        using var scope = As(tenant, "authorization.view");
        var result = await scope.ServiceProvider
            .GetRequiredService<IDomainActionInvoker<ListRoles, IReadOnlyList<RoleInfo>>>()
            .InvokeAsync(new ListRoles());
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    /// <summary>
    ///     A scope with a caller of <paramref name="tenant" /> on it, so the package's actions are invoked
    ///     and authorized as they would be over HTTP.
    /// </summary>
    private IServiceScope As(string tenant, params string[] permissions)
    {
        var scope = Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var claims = new List<Claim>
        {
            new(options.UserIdClaimType, $"catalog-{Guid.NewGuid():N}"),
            new(options.TenantClaimType, tenant),
        };
        claims.AddRange(permissions.Select(p => new Claim(options.PermissionClaimType, p)));

        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "CatalogTest")),
        };
        return scope;
    }
}
