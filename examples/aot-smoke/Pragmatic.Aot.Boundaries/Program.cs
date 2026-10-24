using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using PragmaticBoundaries.Tests;
using PragmaticEntities.Tests;

// Two boundaries on two engines in one native host: Catalog on the Sql engine (SQLite in memory), Reviews on the documents engine
// (MongoDB, PRAGMATIC_AOT_MONGO). The flow of TwoEnginesTests; every check prints, the exit code is the number that failed.
if (Environment.GetEnvironmentVariable("PRAGMATIC_AOT_MONGO") is not { } mongo)
{
    Console.WriteLine("FAIL PRAGMATIC_AOT_MONGO is not set");
    return 1;
}

var failures = 0;
void Check(string what, object? expected, object? actual)
{
    var ok = Equals(expected, actual);
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}: {actual}{(ok ? "" : " (expected " + expected + ")")}");
    failures += ok ? 0 : 1;
}

var database = "b" + Guid.NewGuid().ToString("N")[..12];
var catalog = $"Data Source={database};Mode=Memory;Cache=Shared";
using var keeper = new SqliteConnection(catalog);
keeper.Open();
using (var create = keeper.CreateCommand())
{
    create.CommandText = """
        CREATE TABLE "Products" ("PersistenceId" TEXT PRIMARY KEY, "Name" TEXT NOT NULL, "IsDeleted" INTEGER NOT NULL, "DeletedAt" TEXT NULL,
            "DeletedBy" TEXT NULL, "OwnerId" TEXT NOT NULL);
        """;
    create.ExecuteNonQuery();
}

await using var client = new MongoClient(mongo);

// The host's start, as it would run its migrations: the documents boundary's schema (its indexes) applied before the first request.
// ASSUMPTION: a step of the host here; a migrations target that is not SQL, run with the others, is a later step of the porting.
var reviewsSchema = new DocSession(client, database);
Check("reviews: the schema created at the host's start", false, (await ReviewsBoundaryDocSchema.EnsureAsync(reviewsSchema)).Unchanged);
Check("reviews: the schema unchanged at the next start", true, (await ReviewsBoundaryDocSchema.EnsureAsync(reviewsSchema)).Unchanged);

Guid id;
await using (var anna = Host("anna"))
{
    await using (var scope = anna.CreateAsyncScope())
        id = await scope.ServiceProvider.GetRequiredService<ReviewDesk>().ListAndReviewAsync("Lamp", "Bright", 5);

    await using (var scope = anna.CreateAsyncScope())
    {
        var (product, reviews) = await scope.ServiceProvider.GetRequiredService<ReviewDesk>().ReadAsync("Lamp");
        Check("catalog: the product read back", id, product?.Id);
        Check("catalog: the owner the Sql save stamped", "anna", product?.OwnerId);
        Check("reviews: one review read back", 1, reviews.Count);
        Check("reviews: the owner the documents save stamped", "anna", reviews.Count > 0 ? reviews[0].OwnerId : null);
        Check("catalog: unit of work by key", "SqlUnitOfWork", scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary)).GetType().Name);
        Check("reviews: unit of work by key", "DocUnitOfWork", scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(ReviewsBoundary)).GetType().Name);
    }
}

await using (var bruno = Host("bruno"))
await using (var scope = bruno.CreateAsyncScope())
{
    var (product, _) = await scope.ServiceProvider.GetRequiredService<ReviewDesk>().ReadAsync("Lamp");
    Check("catalog: another caller's product hidden", null, product);
    Check("reviews: another caller's reviews hidden", 0, (await scope.ServiceProvider.GetRequiredService<IReadRepository<ProductReview>>().FindAsync(Specs.Of(id))).Count);
}

Console.WriteLine(failures == 0 ? "BOUNDARIES-AOT-OK" : $"{failures} failed");
return failures;

ServiceProvider Host(string user)
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
    services.AddReviewsBoundaryPersistence((_, filters) => new DocSession(client, database) { Filters = filters });
    services.AddScoped<ReviewDesk>();
    return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
}
