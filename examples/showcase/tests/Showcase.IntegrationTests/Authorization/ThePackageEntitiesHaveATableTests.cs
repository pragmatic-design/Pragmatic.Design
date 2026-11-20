using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Identity;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization.Management.Actions.Permissions;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Showcase.Accounts;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     An imported package that owns entities gets them stored by the importing boundary's engine.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>Showcase.Accounts</c> declares
///         <c>[UsePackage&lt;AuthorizationManagementPackage, AccountsBoundary&gt;]</c>, and the package's
///         operations read and write through <c>IAuthorizationManagementStore</c>, keyed by that boundary:
///         on EF Core over the boundary's <c>DbContext</c>, on the Sql and documents engines over their own
///         rows. Resolving the store is one half; having something to read in it is this. On EF Core,
///         without the entities declared, <c>Set&lt;T&gt;()</c> throws because the type is not in the
///         model — and the generated schema snapshot shows it first, by not changing at all when the import
///         is added.
///     </para>
///     <para>
///         The action is invoked in process rather than over HTTP because it declares no
///         <c>[Endpoint]</c>: what is being measured is the persistence, not a route. The lengths the
///         columns are bounded by, which EF Core's model carries, are <c>ThePackagesColumnLengthsTests</c>.
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
            "the action writes through the store of the boundary that imported the package, but it "
            + $"answered: {(created.IsFailure ? created.Error?.ToString() : "")}");

        // Read in a scope of its own: what comes back is what was stored, not what the action held.
        using var read = Services.CreateScope();
        var permissions = await StoreOf(read).GetPermissionsAsync("test-tenant");
        var row = permissions.FirstOrDefault(p => p.PersistenceId == created.Value);

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
    ///     A role's name is unique in its tenant in the store, not only in <c>CreateRole</c>'s check, which two concurrent requests
    ///     can both pass (#490): the second row, written past the operation, is refused by the unique index on (Name, TenantId).
    /// </summary>
    /// <remarks>
    ///     The two roles have keys of their own (<c>Guid.CreateVersion7()</c>), so a refusal is the index's, not the primary key's. The
    ///     exception each engine raises is its own, so what is asserted is that the write is refused and one row is left.
    /// </remarks>
    [Fact]
    public async Task ASecondRoleOfTheSameNameInATenant_IsRefusedByTheDatabase()
    {
        var name = $"auditor-{Guid.NewGuid():N}"[..20];
        var tenant = $"acme-{Guid.NewGuid():N}"[..17];

        using (var scope = Services.CreateScope())
            await StoreOf(scope).AddRoleAsync(new DynamicRole { Name = name, TenantId = tenant });

        using (var scope = Services.CreateScope())
        {
            var refused = await Record.ExceptionAsync(
                async () => await StoreOf(scope).AddRoleAsync(new DynamicRole { Name = name, TenantId = tenant }));
            refused.Should().NotBeNull("the unique index on (Name, TenantId) refuses the second row");
        }

        using (var scope = Services.CreateScope())
        {
            var roles = await StoreOf(scope).GetRolesAsync(tenant);
            roles.Where(r => r.Name == name).Should().ContainSingle("the first row stays, and the second was not written");
        }
    }

    /// <summary>
    ///     The control: each of the package's entities has somewhere to be read from on the importing boundary's engine, which is what
    ///     makes a row readable at all.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it, "the create succeeded" could be satisfied by an action that swallowed the
    ///     failure — and the defect being closed here is precisely a type the boundary does not store. Every read of the store is made, so
    ///     each of the seven sets answers; an unknown one throws.
    /// </remarks>
    [Fact]
    public async Task TheImportingBoundary_StoresEveryOneOfThePackagesEntities()
    {
        using var scope = Services.CreateScope();
        var store = StoreOf(scope);
        var tenant = $"none-{Guid.NewGuid():N}"[..17];
        var nobody = $"nobody-{Guid.NewGuid():N}";
        var nothing = $"nothing-{Guid.NewGuid():N}";

        (await store.GetPermissionsAsync(tenant)).Should().NotBeNull();
        (await store.GetRolesAsync(tenant)).Should().NotBeNull();
        (await store.GetRolePermissionsAsync([nothing], tenant)).Should().BeEmpty();
        (await store.GetUserRolesAsync(nobody, tenant)).Should().BeEmpty();
        (await store.GetUserGroupsAsync(nobody, tenant)).Should().BeEmpty();
        (await store.GetGroupsAsync(tenant)).Should().NotBeNull();
        (await store.GetGroupRolesAsync([nothing], tenant)).Should().BeEmpty();
    }

    /// <summary>The package's store for the boundary that imported it.</summary>
    private static IAuthorizationManagementStore StoreOf(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredKeyedService<IAuthorizationManagementStore>(typeof(AccountsBoundary));

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
}
