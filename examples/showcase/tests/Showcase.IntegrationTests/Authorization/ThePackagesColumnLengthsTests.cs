using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Management.Entities;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     The package's entities declare their column lengths (#590), and EF Core's model of the importing boundary bounds the columns with them.
/// </summary>
/// <remarks>
///     Read from EF Core's model: a mechanism of the provider. On the Sql and documents engines the package's rows are its own types
///     (<c>Pragmatic.Authorization.Management.Engines</c>), which declare their lengths themselves. That the package's entities are stored at
///     all, on every engine, is <c>ThePackageEntitiesHaveATableTests</c>.
/// </remarks>
public class ThePackagesColumnLengthsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    ///     The package's EF Core configurations declared the lengths once and were never applied, then removed (#527): every column was
    ///     unbounded.
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
}
