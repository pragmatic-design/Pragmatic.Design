using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     The host of two boundaries on two engines: each registered by the method the generator emitted for its engine, each unit of work resolved
///     by its boundary's key, each entity's repository by its type, and the filter families of both applied to the caller the services describe.
/// </summary>
public sealed class TwoEnginesTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    private const string Schema = """
        CREATE TABLE "Products" ("PersistenceId" TEXT PRIMARY KEY, "Name" TEXT NOT NULL, "IsDeleted" INTEGER NOT NULL, "DeletedAt" TEXT NULL,
            "DeletedBy" TEXT NULL, "OwnerId" TEXT NOT NULL);
        """;

    [Fact]
    public async Task One_host_writes_and_reads_a_boundary_on_each_engine_through_Pragmatics_contracts()
    {
        // One in-memory database for the test, kept alive by this connection; each session (one per scope) opens its own on it.
        var database = "b" + Guid.NewGuid().ToString("N")[..12];
        var catalog = $"Data Source={database};Mode=Memory;Cache=Shared";
        using var keeper = new SqliteConnection(catalog);
        keeper.Open();
        using (var create = keeper.CreateCommand())
        {
            create.CommandText = Schema;
            create.ExecuteNonQuery();
        }

        await using var provider = Host(catalog, database, "anna");

        Guid id;
        await using (var scope = provider.CreateAsyncScope())
            id = await scope.ServiceProvider.GetRequiredService<ReviewDesk>().ListAndReviewAsync("Lamp", "Bright", 5);

        await using (var scope = provider.CreateAsyncScope())
        {
            var (product, reviews) = await scope.ServiceProvider.GetRequiredService<ReviewDesk>().ReadAsync("Lamp");
            Assert.Equal(id, product?.Id);
            Assert.Equal("anna", product!.OwnerId); // the Sql engine's save stamped the owner
            var review = Assert.Single(reviews);
            Assert.Equal(("Bright", 5, "anna"), (review.Text, review.Stars, review.OwnerId)); // and the documents engine's
            Assert.IsType<Pragmatic.Persistence.Sql.Pragmatic.SqlUnitOfWork>(scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary)));
            Assert.IsType<Pragmatic.Persistence.Documents.Pragmatic.DocUnitOfWork>(scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(ReviewsBoundary)));
        }

        // Another caller: the owner family of each boundary, on its engine, hides anna's rows.
        await using var other = Host(catalog, database, "bruno");
        await using (var scope = other.CreateAsyncScope())
        {
            var (product, _) = await scope.ServiceProvider.GetRequiredService<ReviewDesk>().ReadAsync("Lamp");
            Assert.Null(product);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IReadRepository<ProductReview>>().FindAsync(Specs.Of(id)));
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
        services.AddScoped<IQueryFilterToggle, QueryFilterToggle>();
        services.AddCatalogBoundaryPersistence((_, filters) => new SqlSession(Open(catalog), SqlDialect.Sqlite) { Filters = filters });
        services.AddReviewsBoundaryPersistence((_, filters) => new DocSession(mongo.Client, database) { Filters = filters });
        services.AddScoped<ReviewDesk>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
