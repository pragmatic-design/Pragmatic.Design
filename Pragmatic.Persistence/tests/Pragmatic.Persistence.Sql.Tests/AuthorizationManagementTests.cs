using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Management.Engines.Sql;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic.Authorization.Management on the Sql engine (Pragmatic.Authorization.Management.Engines), on five databases: its tables as
///     the generated schema has them in the database of the boundary that imports it (EF Core's tables for the package's entities), migrated
///     with the others, and its operations over them (<see cref="AuthorizationManagementScenario" />).
/// </summary>
[Collection("pragmatic databases")]
public class AuthorizationManagementTests(PragmaticDatabases databases)
{
    /// <summary>
    ///     EF Core's tables for the package's five entities, as Pragmatic's EF branch describes them on PostgreSQL (the Showcase's
    ///     <c>ShowcaseAppDatabase</c> schema): the same names, types, nullability and keys, and no index, so a database holds one set of
    ///     roles whichever engine writes it.
    /// </summary>
    [Fact]
    public void The_packages_tables_in_the_schema_are_EF_Cores_column_for_column()
    {
        var tables = DialectSchemas.For(SqlDialect.PostgreSql).Tables;
        (string, string, bool, bool)[] Columns(string table)
        {
            var schema = tables.Single(t => t.Name == table);
            Assert.Empty(schema.Indexes);
            return [.. schema.Columns.Select(c => (c.Name, c.SqlType, c.IsNullable, c.IsPrimaryKey))];
        }

        (string, string, bool, bool)[] audited =
        [
            ("PersistenceId", "uuid", false, true), ("IsDeleted", "boolean", false, false), ("DeletedAt", "timestamptz", true, false),
            ("DeletedBy", "text", true, false), ("Name", "text", false, false), ("Description", "text", true, false),
        ];
        (string, string, bool, bool)[] stamps =
        [
            ("TenantId", "text", true, false), ("CreatedAt", "timestamptz", false, false), ("CreatedBy", "text", true, false),
            ("UpdatedAt", "timestamptz", true, false), ("UpdatedBy", "text", true, false),
        ];
        Assert.Equal([.. audited, .. stamps], Columns("DynamicRoles"));
        Assert.Equal([.. audited, ("Category", "text", true, false), .. stamps], Columns("DynamicPermissions"));
        Assert.Equal([("PersistenceId", "uuid", false, true), ("RoleName", "text", false, false), ("PermissionName", "text", false, false),
            ("TenantId", "text", true, false), ("ValidFrom", "timestamptz", false, false), ("ValidTo", "timestamptz", true, false)], Columns("DynamicRolePermissions"));
        Assert.Equal([("PersistenceId", "uuid", false, true), ("UserId", "text", false, false), ("RoleName", "text", false, false),
            ("TenantId", "text", true, false), ("AssignedBy", "text", true, false), ("ValidFrom", "timestamptz", false, false),
            ("ValidTo", "timestamptz", true, false)], Columns("UserRoleAssignments"));
        Assert.Equal([("PersistenceId", "uuid", false, true), ("UserId", "text", false, false), ("GroupName", "text", false, false),
            ("TenantId", "text", true, false), ("AssignedBy", "text", true, false), ("ValidFrom", "timestamptz", false, false),
            ("ValidTo", "timestamptz", true, false)], Columns("UserGroupAssignments"));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Roles_and_permissions_are_created_assigned_listed_and_revoked(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        await using var provider = Store(session);
        await using var scope = provider.CreateAsyncScope();

        await AuthorizationManagementScenario.RunAsync(scope.ServiceProvider.GetRequiredKeyedService<IAuthorizationManagementStore>(typeof(AuthorizationManagementTests)));
    }

    /// <summary>The package's store on <paramref name="session" />, keyed by a boundary (this class stands for its key), as the generated registration adds it.</summary>
    private static ServiceProvider Store(SqlSession session)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<SqlSession>(typeof(AuthorizationManagementTests), session); // an instance: the scope does not dispose the test's session
        services.AddSqlAuthorizationManagement<AuthorizationManagementTests>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
