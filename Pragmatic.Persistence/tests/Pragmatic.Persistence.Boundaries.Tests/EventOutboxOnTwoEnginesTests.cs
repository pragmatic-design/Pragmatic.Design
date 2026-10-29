using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Events.Engines;
using Pragmatic.Events.Engines.Documents;
using Pragmatic.Events.Extensions;
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
///     The event outbox of two <c>[EnableEventOutbox]</c> boundaries on two engines, as the generated <c>Add{Boundary}Persistence</c> registers
///     it: each save writes its entities' events to its own boundary's outbox (the catalog's table on SQLite, the reviews' collection on MongoDB)
///     instead of dispatching them, and each boundary's drainer delivers them, once, under the tenant they were raised in.
/// </summary>
public sealed class EventOutboxOnTwoEnginesTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    // The catalog's tables as SQLite holds them (the outbox's is EF Core's __EventOutbox: its instants are ticks).
    private const string Schema = """
        CREATE TABLE "Products" ("PersistenceId" TEXT PRIMARY KEY, "Name" TEXT NOT NULL, "IsDeleted" INTEGER NOT NULL, "DeletedAt" TEXT NULL,
            "DeletedBy" TEXT NULL, "OwnerId" TEXT NOT NULL);
        CREATE TABLE "__EventOutbox" ("Id" TEXT PRIMARY KEY, "EventType" TEXT NOT NULL, "Payload" TEXT NOT NULL, "OccurredAt" INTEGER NOT NULL,
            "TenantId" TEXT NULL, "TraceParent" TEXT NULL, "CreatedAt" INTEGER NOT NULL, "ProcessedAt" INTEGER NULL, "Attempts" INTEGER NOT NULL,
            "LastError" TEXT NULL, "ClaimedBy" TEXT NULL, "ClaimedUntil" INTEGER NULL);
        """;

    [Fact]
    public async Task Each_boundary_writes_its_events_to_the_outbox_of_its_engine_and_its_drainer_delivers_them_once()
    {
        using var catalog = Catalog();
        var database = Database();
        await using var provider = Host(catalog.ConnectionString, database, handlers: true);
        using (TenantScope.BeginScope("acme"))
            await ListAndReviewAsync(provider);

        var deliveries = provider.GetRequiredService<Deliveries>().Received;
        Assert.Empty(deliveries); // nothing dispatched in process: the outboxes deliver
        Assert.Equal(1, Scalar(catalog, "SELECT COUNT(*) FROM \"__EventOutbox\" WHERE \"ProcessedAt\" IS NULL")); // the catalog's event only

        var catalogDrainer = provider.GetRequiredService<IEngineEventOutboxDrainer<CatalogBoundary>>();
        Assert.Equal(1, await catalogDrainer.DrainOnceAsync());
        Assert.Equal(0, await catalogDrainer.DrainOnceAsync());
        Assert.Equal((new ProductListed("Lamp", DateTimeOffset.UnixEpoch), "acme"), Assert.Single(deliveries));
        Assert.Equal(0, Scalar(catalog, "SELECT COUNT(*) FROM \"__EventOutbox\" WHERE \"ProcessedAt\" IS NULL"));

        var reviewsDrainer = provider.GetRequiredService<IEngineEventOutboxDrainer<ReviewsBoundary>>();
        Assert.Equal(1, await reviewsDrainer.DrainOnceAsync());
        Assert.Equal(0, await reviewsDrainer.DrainOnceAsync());
        Assert.Equal([(new ProductListed("Lamp", DateTimeOffset.UnixEpoch), "acme"), (new ProductReviewed("Bright", 5, DateTimeOffset.UnixEpoch), "acme")], deliveries);
        var document = Assert.Single(await OutboxAsync(provider));
        Assert.NotNull(document.ProcessedAt);
    }

    [Fact]
    public async Task A_row_another_worker_holds_is_left_to_it_until_its_lease_runs_out()
    {
        using var catalog = Catalog();
        var database = Database();
        await using var provider = Host(catalog.ConnectionString, database, handlers: true);
        await ListAndReviewAsync(provider);
        var held = DateTimeOffset.UtcNow.AddHours(1).UtcTicks;
        Execute(catalog, $"UPDATE \"__EventOutbox\" SET \"ClaimedBy\" = 'other', \"ClaimedUntil\" = {held}");
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(ReviewsBoundary)).Query<EventOutboxDocument>()
                .ExecuteUpdateAsync((s, until) => s.Set(d => d.ClaimedBy, "other").Set(d => d.ClaimedUntil, until), (long?)held);
        }

        var catalogDrainer = provider.GetRequiredService<IEngineEventOutboxDrainer<CatalogBoundary>>();
        var reviewsDrainer = provider.GetRequiredService<IEngineEventOutboxDrainer<ReviewsBoundary>>();
        Assert.Equal(0, await catalogDrainer.DrainOnceAsync());
        Assert.Equal(0, await reviewsDrainer.DrainOnceAsync());
        Assert.Empty(provider.GetRequiredService<Deliveries>().Received);

        // The other worker's lease runs out (it crashed): the rows are anyone's again.
        var lapsed = DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks;
        Execute(catalog, $"UPDATE \"__EventOutbox\" SET \"ClaimedUntil\" = {lapsed}");
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(ReviewsBoundary)).Query<EventOutboxDocument>()
                .ExecuteUpdateAsync((s, until) => s.Set(d => d.ClaimedUntil, until), (long?)lapsed);
        }

        Assert.Equal(1, await catalogDrainer.DrainOnceAsync());
        Assert.Equal(1, await reviewsDrainer.DrainOnceAsync());
        Assert.Equal(2, provider.GetRequiredService<Deliveries>().Received.Count);
    }

    [Fact]
    public async Task An_event_no_handler_takes_stays_pending_with_its_attempt_counted_and_its_claim_released()
    {
        using var catalog = Catalog();
        var database = Database();
        await using var provider = Host(catalog.ConnectionString, database, handlers: false);
        await ListAndReviewAsync(provider);

        Assert.Equal(0, await provider.GetRequiredService<IEngineEventOutboxDrainer<CatalogBoundary>>().DrainOnceAsync());
        Assert.Equal(0, await provider.GetRequiredService<IEngineEventOutboxDrainer<ReviewsBoundary>>().DrainOnceAsync());

        Assert.Equal(1, Scalar(catalog, "SELECT COUNT(*) FROM \"__EventOutbox\" WHERE \"ProcessedAt\" IS NULL AND \"Attempts\" = 1 AND \"ClaimedBy\" IS NULL "
            + "AND \"ClaimedUntil\" IS NULL AND \"LastError\" LIKE 'No registered handler%'"));
        var document = Assert.Single(await OutboxAsync(provider));
        Assert.Equal((null, 1, null, null), (document.ProcessedAt, document.Attempts, document.ClaimedBy, document.ClaimedUntil));
        Assert.StartsWith("No registered handler", document.LastError, StringComparison.Ordinal);
    }

    /// <summary>A product listed in the catalog and reviewed, each boundary's save on its engine: each raises one event.</summary>
    private static async Task ListAndReviewAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var product = new Product { Name = "Lamp" };
        product.List();
        scope.ServiceProvider.GetRequiredService<IRepository<Product>>().Add(product);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary)).SaveChangesAsync();
        Assert.Empty(product.DomainEvents); // taken by the catalog's outbox

        var review = new ProductReview { Text = "Bright", Stars = 5 };
        review.SetProductId(product.Id);
        review.Post();
        scope.ServiceProvider.GetRequiredService<IRepository<ProductReview>>().Add(review);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(ReviewsBoundary)).SaveChangesAsync();
        Assert.Empty(review.DomainEvents); // taken by the reviews' outbox
    }

    private static async Task<List<EventOutboxDocument>> OutboxAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(ReviewsBoundary)).Query<EventOutboxDocument>().ToListAsync();
    }

    private ServiceProvider Host(string catalog, string database, bool handlers)
    {
        static SqliteConnection Open(string connectionString)
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new TestUser("anna"));
        services.AddSingleton<ITenantContext>(new TenantScope());
        services.AddScoped<IQueryFilterToggle, QueryFilterToggle>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<Deliveries>();
        services.AddInMemoryDomainEvents();
        if (handlers)
        {
            services.AddDomainEventHandler<DeliveryRecorder, ProductListed>();
            services.AddDomainEventHandler<DeliveryRecorder, ProductReviewed>();
        }

        services.AddCatalogBoundaryPersistence((_, filters) => new SqlSession(Open(catalog), SqlDialect.Sqlite) { Filters = filters });
        services.AddReviewsBoundaryPersistence((_, filters) => new DocSession(mongo.Client, database) { Filters = filters });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>An in-memory SQLite database of the test's own with the catalog's tables, kept alive by the returned connection.</summary>
    private static SqliteConnection Catalog()
    {
        var keeper = new SqliteConnection($"Data Source={Database()};Mode=Memory;Cache=Shared");
        keeper.Open();
        using var create = keeper.CreateCommand();
        create.CommandText = Schema;
        create.ExecuteNonQuery();
        return keeper;
    }

    private static string Database() => "o" + Guid.NewGuid().ToString("N")[..12];

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
