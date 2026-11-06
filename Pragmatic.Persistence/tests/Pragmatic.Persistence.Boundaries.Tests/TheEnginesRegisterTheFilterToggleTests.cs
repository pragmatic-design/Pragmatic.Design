using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     A boundary's generated <c>Add{Boundary}Persistence</c> registers the query filter toggle its filters read, on each engine (#554): the
///     host below registers nothing else of persistence's, as the hosts of the Showcase on the engines do.
/// </summary>
/// <remarks>
///     Without it the engines' filters were built with no toggle, so nothing could lift a filter there, and application code that takes the
///     toggle stopped the host at container validation. The other tests of this suite register the toggle by hand, which is why none saw it.
/// </remarks>
public sealed class TheEnginesRegisterTheFilterToggleTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    private const string Schema = """
        CREATE TABLE "Products" ("PersistenceId" TEXT PRIMARY KEY, "Name" TEXT NOT NULL, "IsDeleted" INTEGER NOT NULL, "DeletedAt" TEXT NULL,
            "DeletedBy" TEXT NULL, "OwnerId" TEXT NOT NULL);
        """;

    [Fact]
    public async Task The_toggle_resolved_from_the_generated_registrations_lifts_the_owner_filter_on_both_engines()
    {
        var database = "t" + Guid.NewGuid().ToString("N")[..12];
        var catalog = $"Data Source={database};Mode=Memory;Cache=Shared";
        using var keeper = new SqliteConnection(catalog);
        keeper.Open();
        using (var create = keeper.CreateCommand())
        {
            create.CommandText = Schema;
            create.ExecuteNonQuery();
        }

        await using (var anna = Host(catalog, database, "anna"))
        await using (var scope = anna.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ReviewDesk>().ListAndReviewAsync("Lamp", "Bright", 5);

        await using var bruno = Host(catalog, database, "bruno");
        await using (var scope = bruno.CreateAsyncScope())
        {
            // The control: with every filter on, anna's rows are not bruno's, on either engine.
            var (hidden, _) = await scope.ServiceProvider.GetRequiredService<ReviewDesk>().ReadAsync("Lamp");
            Assert.Null(hidden);
        }

        await using (var scope = bruno.CreateAsyncScope())
        {
            var toggle = scope.ServiceProvider.GetService<IQueryFilterToggle>();
            Assert.NotNull(toggle); // registered by the boundaries' generated registrations, and by nothing else here

            using (toggle.UseMode(FilterMode.Admin)) // the owner family lifted
            {
                var (product, reviews) = await scope.ServiceProvider.GetRequiredService<ReviewDesk>().ReadAsync("Lamp");
                Assert.Equal("anna", product?.OwnerId); // the Sql engine's filter read the toggle
                Assert.Equal("anna", Assert.Single(reviews).OwnerId); // and the documents engine's
            }
        }
    }

    private ServiceProvider Host(string catalog, string database, string user)
    {
        static SqliteConnection Open(string connectionString)
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new TestUser(user));
        services.AddSingleton<ITenantContext>(new TenantScope());
        services.AddCatalogBoundaryPersistence((_, filters) => new SqlSession(Open(catalog), SqlDialect.Sqlite) { Filters = filters });
        services.AddReviewsBoundaryPersistence((_, filters) => new DocSession(mongo.Client, database) { Filters = filters });
        services.AddScoped<ReviewDesk>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
