using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Invoker;
using Pragmatic.Authorization.Management.Actions.Roles;
using Pragmatic.Authorization.Management.Engines.Documents;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Identity;
using Pragmatic.Persistence.Documents;
using PragmaticBoundaries.Tests.StaffOnDocuments;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     Pragmatic.Authorization.Management imported into a boundary on the documents engine (<see cref="StaffOnDocumentsModule" />), with no
///     EF Core referenced, on MongoDB: the boundary's generated registration adds the store of its engine and answers the package's
///     operations with it, its schema holds the package's collections, and the operations run over them.
/// </summary>
public sealed class AuthorizationManagementOnTheDocumentsEngineTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    [Fact]
    public void The_boundarys_schema_holds_the_packages_collections()
        => Assert.Equal(["Desks", "DynamicPermissions", "DynamicRolePermissions", "DynamicRoles", "UserGroupAssignments", "UserRoleAssignments"],
            StaffOnDocumentsBoundaryDocSchema.Collections.Select(c => c.Collection).Order(StringComparer.Ordinal));

    /// <summary>The package's operation, resolved as the application resolves it, writes through the documents engine's store.</summary>
    [Fact]
    public async Task The_boundarys_registration_answers_the_packages_operations_with_its_engines_store()
    {
        await using var provider = Boundary(new DocSession(mongo.Client, "a" + Guid.NewGuid().ToString("N")[..12]));
        await using var scope = provider.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IAuthorizationManagementStore>();
        Assert.IsType<DocAuthorizationManagementStore>(store);

        var createRole = scope.ServiceProvider.GetRequiredService<IDomainActionInvoker<CreateRole, Guid>>();
        var created = await createRole.InvokeAsync(new CreateRole { Name = "auditor" });

        Assert.Equal(created.Value, Assert.Single(await store.GetRolesAsync(null)).PersistenceId);
    }

    [Fact]
    public async Task Roles_and_permissions_are_created_assigned_listed_and_revoked()
    {
        await using var provider = Boundary(new DocSession(mongo.Client, "a" + Guid.NewGuid().ToString("N")[..12]));
        await using var scope = provider.CreateAsyncScope();

        await AuthorizationManagementScenario.RunAsync(scope.ServiceProvider.GetRequiredService<IAuthorizationManagementStore>());
    }

    /// <summary>The staff boundary's generated registration over <paramref name="session" />, as the application's host calls it.</summary>
    private static ServiceProvider Boundary(DocSession session)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DocSession>(typeof(StaffOnDocumentsBoundary), session); // an instance: the scope does not dispose the test's session
        services.AddSingleton<ICurrentUser>(new TestUser("admin"));
        services.AddStaffOnDocumentsBoundary();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
