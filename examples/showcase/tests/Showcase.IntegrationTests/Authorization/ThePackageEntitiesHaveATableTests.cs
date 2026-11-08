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
    ///     A role's name is unique in its tenant in the database, not only in <c>CreateRole</c>'s check, which two concurrent requests
    ///     can both pass (#490): the second row, written past the operation, is refused by the unique index on (Name, TenantId).
    /// </summary>
    [Fact]
    public async Task ASecondRoleOfTheSameNameInATenant_IsRefusedByTheDatabase()
    {
        var name = $"auditor-{Guid.NewGuid():N}"[..20];
        var tenant = $"acme-{Guid.NewGuid():N}"[..17];

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(Showcase.Accounts.AccountsBoundary));
        context.Set<DynamicRole>().Add(new DynamicRole { Name = name, TenantId = tenant });
        await context.SaveChangesAsync();

        context.Set<DynamicRole>().Add(new DynamicRole { Name = name, TenantId = tenant });
        var refused = await Record.ExceptionAsync(() => context.SaveChangesAsync());

        refused.Should().BeOfType<DbUpdateException>();
        refused!.InnerException!.Message.Should().Contain("IX_DynamicRoles_Name_TenantId", "the database's unique index refused it");
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
    ///     The package's entities declare their column lengths (#590), and EF Core's model of the importing boundary bounds the columns with them.
    ///     The package's EF Core configurations declared them once and were never applied, then removed (#527): every column was unbounded.
    /// </summary>
    [Fact]
    public void ThePackagesColumns_AreBoundedByTheLengthsTheEntitiesDeclare()
    {
        using var scope = Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(Showcase.Accounts.AccountsBoundary)).Model;
        int? Length<T>(string property) => model.FindEntityType(typeof(T))!.FindProperty(property)!.GetMaxLength();

        Length<DynamicRole>(nameof(DynamicRole.Name)).Should().Be(256);
        Length<DynamicRole>(nameof(DynamicRole.Description)).Should().Be(500);
        Length<DynamicPermission>(nameof(DynamicPermission.Category)).Should().Be(128);
        Length<DynamicRolePermission>(nameof(DynamicRolePermission.PermissionName)).Should().Be(256);
        Length<UserRoleAssignment>(nameof(UserRoleAssignment.AssignedBy)).Should().Be(256);
        Length<UserGroupAssignment>(nameof(UserGroupAssignment.TenantId)).Should().Be(128);
        Length<DynamicRole>(nameof(DynamicRole.CreatedBy)).Should().BeNull("the control: an attribution was never bounded");
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
