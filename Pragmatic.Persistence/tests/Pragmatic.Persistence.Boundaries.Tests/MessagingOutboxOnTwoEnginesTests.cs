using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.ControlPlane;
using Pragmatic.Identity;
using Pragmatic.Messaging;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Engines.Documents;
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
///     The Messaging outbox of two <c>[EnableOutbox]</c> boundaries on two engines, as the generated <c>Add{Boundary}Persistence</c> registers
///     it: each save writes its entities' events to its own boundary's outbox (the orders' table on SQLite, the shipments' collection on MongoDB)
///     instead of dispatching them, and the one delivery pump the application has reads both sources and publishes each message once, under the
///     tenant it was raised in.
/// </summary>
public sealed class MessagingOutboxOnTwoEnginesTests(MongoServer mongo) : IClassFixture<MongoServer>
{
    // The orders' tables as SQLite holds them (the outbox's is EF Core's __OutboxMessages: its instants are ticks).
    private const string Schema = """
        CREATE TABLE "Orders" ("PersistenceId" TEXT PRIMARY KEY, "Number" TEXT NOT NULL);
        CREATE TABLE "__OutboxMessages" ("Id" TEXT PRIMARY KEY, "MessageType" TEXT NOT NULL, "Payload" TEXT NOT NULL, "CreatedAt" INTEGER NOT NULL,
            "ProcessedAt" INTEGER NULL, "RetryCount" INTEGER NOT NULL, "NextAttemptAt" INTEGER NULL, "Error" TEXT NULL, "ClaimedBy" TEXT NULL,
            "ClaimedUntil" INTEGER NULL, "CorrelationId" TEXT NULL, "TenantId" TEXT NULL, "UserId" TEXT NULL, "HeadersJson" TEXT NULL);
        """;

    [Fact]
    public async Task Each_boundary_writes_its_events_to_the_outbox_of_its_engine_and_the_pump_publishes_each_once()
    {
        using var orders = Orders();
        await using var provider = Host(orders.ConnectionString, Database(), out var bus);
        using (TenantScope.BeginScope("acme"))
            await PlaceAndShipAsync(provider);

        Assert.Empty(bus.Published); // nothing published in the save: the outboxes hold the events
        Assert.Equal(1, Scalar(orders, "SELECT COUNT(*) FROM \"__OutboxMessages\" WHERE \"ProcessedAt\" IS NULL AND \"TenantId\" = 'acme' AND \"UserId\" = 'anna'"));
        var health = Assert.Single(provider.GetServices<IHostHealthContributor>(), c => c.Name == "Messaging.Outbox");
        Assert.Equal(2, (await health.CheckAsync()).Data!["totalPending"]); // one source per boundary, both registered

        var pump = provider.GetServices<IHostedService>().OfType<OutboxDeliveryService>().Single();
        await pump.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(async () => (await PendingAsync(provider)) == 0, TimeSpan.FromSeconds(15));
        }
        finally
        {
            await pump.StopAsync(CancellationToken.None);
        }

        Assert.Equal(
            [("acme", (object)new OrderPlaced("A-1", DateTimeOffset.UnixEpoch)), ("acme", new ShipmentSent("Parcel", DateTimeOffset.UnixEpoch))],
            bus.Published.Select(p => (p.Context.TenantId, p.Message)).OrderBy(p => p.Message.GetType().Name));
        Assert.All(bus.Published, p => Assert.Equal("anna", p.Context.UserId));
    }

    [Fact]
    public async Task A_row_another_worker_holds_is_left_to_it_until_its_lease_runs_out()
    {
        using var orders = Orders();
        await using var provider = Host(orders.ConnectionString, Database(), out _);
        await PlaceAndShipAsync(provider);
        var held = DateTimeOffset.UtcNow.AddHours(1).UtcTicks;
        Execute(orders, $"UPDATE \"__OutboxMessages\" SET \"ClaimedBy\" = 'other', \"ClaimedUntil\" = {held}");
        await ShipmentsOutbox(provider, (s, until) => s.Query<OutboxMessageDocument>()
            .ExecuteUpdateAsync((u, a) => u.Set(d => d.ClaimedBy, "other").Set(d => d.ClaimedUntil, a), until), held);

        var whileHeld = await PollEachSourceAsync(provider);
        Assert.Equal([0, 0], whileHeld);

        // The other worker's lease runs out (it crashed): the rows are anyone's again.
        var lapsed = DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks;
        Execute(orders, $"UPDATE \"__OutboxMessages\" SET \"ClaimedUntil\" = {lapsed}");
        await ShipmentsOutbox(provider, (s, until) => s.Query<OutboxMessageDocument>().ExecuteUpdateAsync((u, a) => u.Set(d => d.ClaimedUntil, a), until), lapsed);

        var afterTheLease = await PollEachSourceAsync(provider);
        Assert.Equal([1, 1], afterTheLease);
    }

    [Fact]
    public async Task A_failed_publish_is_counted_backed_off_and_released_and_a_delivered_one_is_purged_on_both_engines()
    {
        using var orders = Orders();
        await using var provider = Host(orders.ConnectionString, Database(), out _);
        await PlaceAndShipAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        foreach (var source in scope.ServiceProvider.GetServices<IOutboxSource>())
        {
            var pending = Assert.Single(await source.GetPendingAsync(10));
            await source.MarkFailedAsync(pending.Id, "the broker refused it");
            Assert.Empty(await source.GetPendingAsync(10)); // released, but inside its backoff window
            var failed = Assert.Single(await source.PeekPendingAsync(10));
            Assert.Equal((1, "the broker refused it", (string?)null, (DateTimeOffset?)null), (failed.RetryCount, failed.Error, failed.ClaimedBy, failed.ClaimedUntil));
            Assert.True(failed.NextAttemptAt > DateTimeOffset.UtcNow, source.BoundaryName);

            await source.MarkProcessedAsync(pending.Id);
            Assert.Equal((0, (DateTimeOffset?)null), await source.InspectPendingAsync());
            Assert.Equal(1, await source.PurgeProcessedAsync(TimeSpan.FromMinutes(-1)));
            Assert.Empty(await source.PeekPendingAsync(10));
        }

        Assert.Equal(0, Scalar(orders, "SELECT COUNT(*) FROM \"__OutboxMessages\""));
        Assert.Empty(await ShipmentsOutbox(provider, (s, _) => s.Query<OutboxMessageDocument>().ToListAsync(), 0L));
    }

    /// <summary>An order placed and a shipment sent, each boundary's save on its engine: each raises one event.</summary>
    private static async Task PlaceAndShipAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var order = new Order { Number = "A-1" };
        order.Place();
        scope.ServiceProvider.GetRequiredService<IRepository<Order>>().Add(order);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(OrdersBoundary)).SaveChangesAsync();
        Assert.Empty(order.DomainEvents); // taken by the orders' outbox

        var shipment = new Shipment { Carrier = "Parcel" };
        shipment.Send();
        scope.ServiceProvider.GetRequiredService<IRepository<Shipment>>().Add(shipment);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(ShipmentsBoundary)).SaveChangesAsync();
        Assert.Empty(shipment.DomainEvents); // taken by the shipments' outbox
    }

    /// <summary>What one poll of each boundary's source claims, in boundary order.</summary>
    private static async Task<int[]> PollEachSourceAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var counts = new List<(string Boundary, int Count)>();
        foreach (var source in scope.ServiceProvider.GetServices<IOutboxSource>())
            counts.Add((source.BoundaryName, (await source.GetPendingAsync(10)).Count));
        return [.. counts.OrderBy(c => c.Boundary, StringComparer.Ordinal).Select(c => c.Count)];
    }

    private static async Task<int> PendingAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var pending = 0;
        foreach (var source in scope.ServiceProvider.GetServices<IOutboxSource>())
            pending += (await source.InspectPendingAsync()).Pending;
        return pending;
    }

    private static async Task<T> ShipmentsOutbox<T, TArg>(ServiceProvider provider, Func<DocSession, TArg, ValueTask<T>> act, TArg arg)
    {
        await using var scope = provider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(ShipmentsBoundary)), arg);
    }

    /// <summary>Polls until <paramref name="condition" /> holds, and throws when it never does, so a timeout is reported as itself.</summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition was still false after {timeout.TotalSeconds:0.#}s.");
    }

    private ServiceProvider Host(string orders, string database, out RecordingBus bus)
    {
        static SqliteConnection Open(string connectionString)
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        bus = new RecordingBus();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new TestUser("anna"));
        services.AddSingleton<ITenantContext>(new TenantScope());
        services.AddScoped<IQueryFilterToggle, QueryFilterToggle>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton<IMessageTypeRegistry>(new OutboxEventTypes());
        services.AddSingleton(Options.Create(new MessagingOptions { PollingIntervalSeconds = 1 }));

        services.AddOrdersBoundaryPersistence((_, filters) => new SqlSession(Open(orders), SqlDialect.Sqlite) { Filters = filters });
        services.AddShipmentsBoundaryPersistence((_, filters) => new DocSession(mongo.Client, database) { Filters = filters });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>An in-memory SQLite database of the test's own with the orders' tables, kept alive by the returned connection.</summary>
    private static SqliteConnection Orders()
    {
        var keeper = new SqliteConnection($"Data Source={Database()};Mode=Memory;Cache=Shared");
        keeper.Open();
        using var create = keeper.CreateCommand();
        create.CommandText = Schema;
        create.ExecuteNonQuery();
        return keeper;
    }

    private static string Database() => "m" + Guid.NewGuid().ToString("N")[..12];

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
