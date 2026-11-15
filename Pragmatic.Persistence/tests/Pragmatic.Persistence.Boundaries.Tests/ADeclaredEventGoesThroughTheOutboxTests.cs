using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Invoker;
using Pragmatic.Events.Engines;
using Pragmatic.Events.Engines.Documents;
using Pragmatic.Events.Extensions;
using Pragmatic.Identity;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Entities;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     An event an operation declares (<c>[Raises&lt;T&gt;]</c> on an action) goes through the boundary's outbox, as the events an entity
///     raises do (#736): written in the operation's save, in its transaction, and delivered by the outbox, not dispatched in process after the
///     commit, where a crash between the commit and the dispatch lost it.
/// </summary>
/// <remarks>
///     The window is held open on every run without a crash: the invocation returns after its commit, and what it would have dispatched in
///     process is either in the outbox, undelivered, or in <see cref="Deliveries" />. Before the change the outbox was empty and the event had
///     been dispatched.
/// </remarks>
public sealed class ADeclaredEventGoesThroughTheOutboxTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    private const string CatalogSchema = """
        CREATE TABLE "Products" ("PersistenceId" TEXT PRIMARY KEY, "Name" TEXT NOT NULL, "IsDeleted" INTEGER NOT NULL, "DeletedAt" TEXT NULL,
            "DeletedBy" TEXT NULL, "OwnerId" TEXT NOT NULL);
        CREATE TABLE "__EventOutbox" ("Id" TEXT PRIMARY KEY, "EventType" TEXT NOT NULL, "Payload" TEXT NOT NULL, "OccurredAt" INTEGER NOT NULL,
            "TenantId" TEXT NULL, "TraceParent" TEXT NULL, "CreatedAt" INTEGER NOT NULL, "ProcessedAt" INTEGER NULL, "Attempts" INTEGER NOT NULL,
            "LastError" TEXT NULL, "ClaimedBy" TEXT NULL, "ClaimedUntil" INTEGER NULL);
        """;

    private const string OrdersSchema = """
        CREATE TABLE "Orders" ("PersistenceId" TEXT PRIMARY KEY, "Number" TEXT NOT NULL);
        CREATE TABLE "__OutboxMessages" ("Id" TEXT PRIMARY KEY, "MessageType" TEXT NOT NULL, "Payload" TEXT NOT NULL, "CreatedAt" INTEGER NOT NULL,
            "ProcessedAt" INTEGER NULL, "RetryCount" INTEGER NOT NULL, "NextAttemptAt" INTEGER NULL, "Error" TEXT NULL, "ClaimedBy" TEXT NULL,
            "ClaimedUntil" INTEGER NULL, "CorrelationId" TEXT NULL, "TenantId" TEXT NULL, "UserId" TEXT NULL, "HeadersJson" TEXT NULL);
        """;

    [Fact]
    public async Task On_the_sql_engine_an_actions_declared_event_is_in_the_event_outbox_after_the_commit_and_the_drainer_delivers_it()
    {
        using var catalog = Database(CatalogSchema);
        using var orders = Database(OrdersSchema);
        await using var provider = Host(catalog.ConnectionString, orders.ConnectionString, Name());
        var product = new Product { Name = "Lamp" };
        await SaveAsync<Product, CatalogBoundary>(provider, product);

        using (TenantScope.BeginScope("acme"))
            await InvokeAsync(provider, new RenameProductAction { Id = product.Id, Name = "Desk lamp" });

        var deliveries = provider.GetRequiredService<Deliveries>().Received;
        Assert.Empty(deliveries); // not dispatched in process: a crash here would have lost it
        Assert.Equal(1, Scalar(catalog, $"SELECT COUNT(*) FROM \"__EventOutbox\" WHERE \"ProcessedAt\" IS NULL AND \"EventType\" LIKE '%{nameof(ProductRenamed)}%'"));

        Assert.Equal(1, await provider.GetRequiredService<IEngineEventOutboxDrainer<CatalogBoundary>>().DrainOnceAsync());
        var (delivered, tenant) = Assert.Single(deliveries);
        var renamed = Assert.IsType<ProductRenamed>(delivered);
        Assert.Equal((product.Id, "Desk lamp", "acme"), (renamed.Id, renamed.Name, tenant));
    }

    [Fact]
    public async Task On_the_documents_engine_an_actions_declared_event_is_in_the_event_outbox_after_the_commit_and_the_drainer_delivers_it()
    {
        using var catalog = Database(CatalogSchema);
        using var orders = Database(OrdersSchema);
        var database = Name();
        await using var provider = Host(catalog.ConnectionString, orders.ConnectionString, database);
        var review = new ProductReview { Text = "Bright", Stars = 5 };
        review.SetProductId(Guid.NewGuid());
        await SaveAsync<ProductReview, ReviewsBoundary>(provider, review);

        await InvokeAsync(provider, new EditReviewAction { Id = review.Id, Text = "Very bright" });

        var deliveries = provider.GetRequiredService<Deliveries>().Received;
        Assert.Empty(deliveries);
        await using (var scope = provider.CreateAsyncScope())
        {
            var outbox = await scope.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(ReviewsBoundary)).Query<EventOutboxDocument>().ToListAsync();
            Assert.Contains(nameof(ReviewEdited), Assert.Single(outbox).EventType, StringComparison.Ordinal);
        }

        Assert.Equal(1, await provider.GetRequiredService<IEngineEventOutboxDrainer<ReviewsBoundary>>().DrainOnceAsync());
        var edited = Assert.IsType<ReviewEdited>(Assert.Single(deliveries).Event);
        Assert.Equal((review.Id, "Very bright"), (edited.Id, edited.Text));
    }

    [Fact]
    public async Task On_the_messaging_outbox_an_actions_declared_event_is_a_pending_message_after_the_commit()
    {
        using var catalog = Database(CatalogSchema);
        using var orders = Database(OrdersSchema);
        await using var provider = Host(catalog.ConnectionString, orders.ConnectionString, Name());
        var order = new Order { Number = "A-1" };
        await SaveAsync<Order, OrdersBoundary>(provider, order);

        using (TenantScope.BeginScope("acme"))
            await InvokeAsync(provider, new RenumberOrderAction { Id = order.Id, Number = "A-2" });

        Assert.Empty(provider.GetRequiredService<Deliveries>().Received);
        Assert.Equal(1, Scalar(orders, $"SELECT COUNT(*) FROM \"__OutboxMessages\" WHERE \"ProcessedAt\" IS NULL AND \"TenantId\" = 'acme' "
            + $"AND \"MessageType\" = '{typeof(OrderRenumbered).FullName}'"));
    }

    /// <summary>
    ///     A mutation nested in an action that owns the commit leaves its declared event in the owner's batch; the owner hands it to the outbox
    ///     with its own (#800). Before, the batch was dispatched in process after the commit, past the outbox.
    /// </summary>
    [Fact]
    public async Task On_the_event_outbox_a_nested_mutations_declared_event_is_in_the_outbox_after_the_owners_commit_and_delivered_once()
    {
        using var catalog = Database(CatalogSchema);
        using var orders = Database(OrdersSchema);
        await using var provider = Host(catalog.ConnectionString, orders.ConnectionString, Name());
        var product = new Product { Name = "Lamp" };
        await SaveAsync<Product, CatalogBoundary>(provider, product);

        using (TenantScope.BeginScope("acme"))
            await InvokeAsync(provider, new RelabelProductAction { Id = product.Id, Name = "Floor lamp" });

        var deliveries = provider.GetRequiredService<Deliveries>().Received;
        Assert.Empty(deliveries); // not dispatched in process after the owner's commit
        Assert.Equal(1, Scalar(catalog, $"SELECT COUNT(*) FROM \"__EventOutbox\" WHERE \"ProcessedAt\" IS NULL AND \"EventType\" LIKE '%{nameof(ProductRenamed)}%'"));

        Assert.Equal(1, await provider.GetRequiredService<IEngineEventOutboxDrainer<CatalogBoundary>>().DrainOnceAsync());
        var (delivered, tenant) = Assert.Single(deliveries);
        var renamed = Assert.IsType<ProductRenamed>(delivered);
        Assert.Equal((product.Id, "Floor lamp", "acme"), (renamed.Id, renamed.Name, tenant));
    }

    /// <summary>The same through a composite, whose generated invoker owns the commit of its steps.</summary>
    [Fact]
    public async Task On_the_event_outbox_a_composites_steps_declared_events_are_in_the_outbox_after_its_commit()
    {
        using var catalog = Database(CatalogSchema);
        using var orders = Database(OrdersSchema);
        await using var provider = Host(catalog.ConnectionString, orders.ConnectionString, Name());
        var lamp = new Product { Name = "Lamp" };
        var desk = new Product { Name = "Desk" };
        await SaveAsync<Product, CatalogBoundary>(provider, lamp);
        await SaveAsync<Product, CatalogBoundary>(provider, desk);

        await InvokeAsync(provider, new RetitleTwoProductsAction
        {
            First = new RetitleProductMutation { Id = lamp.Id, Name = "Floor lamp" },
            Second = new RetitleProductMutation { Id = desk.Id, Name = "Standing desk" },
        });

        var deliveries = provider.GetRequiredService<Deliveries>().Received;
        Assert.Empty(deliveries);
        Assert.Equal(2, Scalar(catalog, $"SELECT COUNT(*) FROM \"__EventOutbox\" WHERE \"ProcessedAt\" IS NULL AND \"EventType\" LIKE '%{nameof(ProductRenamed)}%'"));

        Assert.Equal(2, await provider.GetRequiredService<IEngineEventOutboxDrainer<CatalogBoundary>>().DrainOnceAsync());
        Assert.Equal(["Floor lamp", "Standing desk"], deliveries.Select(d => ((ProductRenamed)d.Event).Name).Order());
    }

    [Fact]
    public async Task On_the_messaging_outbox_a_nested_mutations_declared_event_is_a_pending_message_after_the_owners_commit()
    {
        using var catalog = Database(CatalogSchema);
        using var orders = Database(OrdersSchema);
        await using var provider = Host(catalog.ConnectionString, orders.ConnectionString, Name());
        var order = new Order { Number = "B-1" };
        await SaveAsync<Order, OrdersBoundary>(provider, order);

        using (TenantScope.BeginScope("acme"))
            await InvokeAsync(provider, new ReplaceOrderNumberAction { Id = order.Id, Number = "B-2" });

        Assert.Empty(provider.GetRequiredService<Deliveries>().Received);
        Assert.Equal(1, Scalar(orders, $"SELECT COUNT(*) FROM \"__OutboxMessages\" WHERE \"ProcessedAt\" IS NULL AND \"TenantId\" = 'acme' "
            + $"AND \"MessageType\" = '{typeof(OrderRenumbered).FullName}'"));
    }

    /// <summary>The control: the save that writes nothing new leaves no stage behind, and an entity's own events still go through the outbox.</summary>
    [Fact]
    public async Task An_entitys_own_event_is_still_captured_beside_none_declared()
    {
        using var catalog = Database(CatalogSchema);
        using var orders = Database(OrdersSchema);
        await using var provider = Host(catalog.ConnectionString, orders.ConnectionString, Name());
        var product = new Product { Name = "Lamp" };
        product.List();
        await SaveAsync<Product, CatalogBoundary>(provider, product);

        Assert.Equal(1, Scalar(catalog, $"SELECT COUNT(*) FROM \"__EventOutbox\" WHERE \"EventType\" LIKE '%{nameof(ProductListed)}%'"));
        Assert.Equal(0, Scalar(catalog, $"SELECT COUNT(*) FROM \"__EventOutbox\" WHERE \"EventType\" LIKE '%{nameof(ProductRenamed)}%'"));
    }

    private static async Task InvokeAsync<TAction>(ServiceProvider provider, TAction action) where TAction : Pragmatic.Actions.Abstractions.IVoidExecutable
    {
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IVoidDomainActionInvoker<TAction>>().InvokeAsync(action);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.ToString());
    }

    private static async Task SaveAsync<TEntity, TBoundary>(ServiceProvider provider, TEntity entity) where TEntity : class, Pragmatic.Persistence.Entity.IEntity
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>().Add(entity);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(TBoundary)).SaveChangesAsync();
    }

    private ServiceProvider Host(string catalog, string orders, string database)
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
        services.AddDomainEventHandler<DeliveryRecorder, ProductRenamed>();
        services.AddDomainEventHandler<DeliveryRecorder, ReviewEdited>();
        services.AddDomainEventHandler<DeliveryRecorder, OrderRenumbered>();
        services.AddSingleton<IMessageBus>(new RecordingBus());
        services.AddSingleton<IMessageTypeRegistry>(new OutboxEventTypes());
        services.AddSingleton(Options.Create(new MessagingOptions { PollingIntervalSeconds = 1 }));

        services.AddCatalogBoundaryPersistence((_, filters) => new SqlSession(Open(catalog), SqlDialect.Sqlite) { Filters = filters });
        services.AddReviewsBoundaryPersistence((_, filters) => new DocSession(mongo.Client, database) { Filters = filters });
        services.AddOrdersBoundaryPersistence((_, filters) => new SqlSession(Open(orders), SqlDialect.Sqlite) { Filters = filters });
        services.AddPragmaticBoundariesActions();
        services.AddPragmaticBoundariesMutations();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>An in-memory SQLite database of the test's own with <paramref name="schema" />, kept alive by the returned connection.</summary>
    private static SqliteConnection Database(string schema)
    {
        var keeper = new SqliteConnection($"Data Source={Name()};Mode=Memory;Cache=Shared");
        keeper.Open();
        using var create = keeper.CreateCommand();
        create.CommandText = schema;
        create.ExecuteNonQuery();
        return keeper;
    }

    private static string Name() => "d" + Guid.NewGuid().ToString("N")[..12];

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
