using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Identity;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization.Management.Actions.Permissions;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     An imported package that owns entities gets them mapped into the importing boundary's database.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>Showcase.Accounts</c> declares
///         <c>[UsePackage&lt;AuthorizationManagementPackage, AccountsBoundary&gt;]</c>, and the six
///         operations of that package read and write through <c>DbContext.Set&lt;DynamicPermission&gt;()</c>.
///         Resolving the context is one half; having something to read in it is this. Without the five
///         entities declared, <c>Set&lt;T&gt;()</c> throws because the type is not in the model —
///         and the generated schema snapshot shows it first, by not changing at all when the import
///         is added.
///     </para>
///     <para>
///         The action is invoked in process rather than over HTTP because it declares no
///         <c>[Endpoint]</c>: what is being measured is the persistence, not a route.
///     </para>
/// </remarks>
public class ThePackageEntitiesHaveATableTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>The permission is created through the host, and the row is there afterwards.</summary>
    [Fact]
    public async Task CreatePermission_WritesARowTheDatabaseKeeps()
    {
        var name = $"custom.reports.{Guid.NewGuid():N}"[..24];

        using var scope = Services.CreateScope();
        GivePermissionsTo(scope, "authorization.permissions.manage");

        var invoker = scope.ServiceProvider
            .GetRequiredService<IDomainActionInvoker<CreatePermission, Guid>>();

        // In the caller's own tenant: a permission with no tenant holds in every tenant, and only a
        // caller with no tenant may create one (see the test below).
        var created = await invoker.InvokeAsync(new CreatePermission
        {
            Name = name,
            Description = "Created by the package's own action",
            Category = "reports",
            TenantId = "test-tenant",
        });

        created.IsSuccess.Should().BeTrue(
            "the action writes through the DbContext of the boundary that imported the package, but it "
            + $"answered: {(created.IsFailure ? created.Error?.ToString() : "")}");

        var context = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(
            typeof(Showcase.Accounts.AccountsBoundary));

        var row = await context.Set<DynamicPermission>()
            .FirstOrDefaultAsync(p => p.PersistenceId == created.Value);

        row.Should().NotBeNull("a create that reports success must leave a row behind");
        row!.Name.Should().Be(name);
    }

    /// <summary>
    ///     A permission with no tenant is read in every tenant, so an administrator of one tenant who
    ///     could create it would be granting authority in all of them. Through the host, it is refused.
    /// </summary>
    [Fact]
    public async Task ATenantAdministrator_CannotCreateAGlobalPermission()
    {
        using var scope = Services.CreateScope();
        GivePermissionsTo(scope, "authorization.permissions.manage");

        var invoker = scope.ServiceProvider
            .GetRequiredService<IDomainActionInvoker<CreatePermission, Guid>>();

        var created = await invoker.InvokeAsync(new CreatePermission
        {
            Name = $"custom.global.{Guid.NewGuid():N}"[..24],
            Description = "Would hold in every tenant",
            Category = "reports",
        });

        created.Error.Should().BeOfType<ForbiddenError>();
    }

    /// <summary>
    ///     Puts an authenticated principal on the scope, so an in-process invocation of a permissioned
    ///     action is authorized the same way a request is.
    /// </summary>
    /// <remarks>
    ///     <c>ICurrentUser</c> reads the principal off <c>IHttpContextAccessor</c>, and a scope created
    ///     from the root provider has no HTTP context — so without this the action fails with
    ///     "Authentication required", which says nothing about where its rows are stored.
    /// </remarks>
    private static void GivePermissionsTo(IServiceScope scope, params string[] permissions)
    {
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var claims = new List<Claim>
        {
            new(options.UserIdClaimType, $"package-entities-{Guid.NewGuid():N}"),
            new(options.TenantClaimType, "test-tenant"),
        };
        claims.AddRange(permissions.Select(p => new Claim(options.PermissionClaimType, p)));

        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "PackageEntitiesTest")),
            };
    }

    /// <summary>
    ///     The control: the entity is in the model, which is what makes the row readable at all.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it, "the create succeeded" could be satisfied by an action that swallowed the
    ///     failure — and the defect being closed here is precisely a type the model does not know.
    /// </remarks>
    [Fact]
    public void TheImportingBoundarysModel_KnowsThePackagesEntities()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(
            typeof(Showcase.Accounts.AccountsBoundary));

        var mapped = context.Model.GetEntityTypes().Select(e => e.ClrType).ToList();

        mapped.Should().Contain(typeof(DynamicPermission));
        mapped.Should().Contain(typeof(DynamicRole));
        mapped.Should().Contain(typeof(DynamicRolePermission));
        mapped.Should().Contain(typeof(UserRoleAssignment));
        mapped.Should().Contain(typeof(UserGroupAssignment));
    }
}
