using System.Data.Common;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Events;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.EFCore.Outbox;
using Pragmatic.Messaging.Entities;
using Testcontainers.PostgreSql;
using Xunit;

namespace Pragmatic.Messaging.Tests.Integration;

/// <summary>
///     End-to-end for the boundary-level <c>[EnableOutbox]</c> wiring the SG emits, against a real
///     PostgreSQL database. The exact runtime path <c>MessagingOutboxExtensions.AddMessagingOutbox</c>
///     registers the capture interceptor, the EF outbox source, the delivery pump and the purge
///     service. A domain event raised on an entity is captured into <c>__OutboxMessages</c> in the
///     SAME transaction, delivered to the bus by the pump, then a delivered row is deleted by the
///     retention purge (<c>IOutboxSource.PurgeProcessedAsync</c>).
/// </summary>
#pragma warning disable CA2007
public sealed class MessagingOutboxEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private static ServiceProvider BuildProvider(CapturingBus bus, Action<DbContextOptionsBuilder> configureDb)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Exactly what the SG-generated DbContext registration does: the outbox capture interceptor is
        // added to the context options, and AddMessagingOutbox wires source + pump + purge.
        services.AddDbContext<OrdersDbContext>((sp, o) =>
        {
            configureDb(o);
            o.AddInterceptors(sp.GetRequiredService<OutboxInterceptor>());
        });

        services.AddSingleton<IMessageBus>(bus);
        services.AddSingleton<IMessageTypeRegistry>(new OrderPlacedRegistry());
        services.AddSingleton(Options.Create(new MessagingOptions
        {
            PollingIntervalSeconds = 1,
            OutboxRetention = TimeSpan.Zero,
        }));

        // The call the SG emits per [EnableOutbox] boundary.
        services.AddMessagingOutbox<OrdersDbContext>("Orders");

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Postgres_RaiseEvent_CapturedTransactionally_ThenDeliveredAndPurged()
    {
        var bus = new CapturingBus();
        await using var sp = BuildProvider(bus, o => o.UseNpgsql(_postgres.GetConnectionString()));
        await RunOutboxFlowAsync(sp, bus);
    }

    // EfCoreOutboxSource's claim query must translate on SQLite too. SQLite cannot order DateTimeOffset,
    // so the config value-converts the timestamps to ticks. A shared in-memory connection kept open so
    // every DI scope sees the same database.
    [Fact]
    public async Task Sqlite_RaiseEvent_CapturedTransactionally_ThenDeliveredAndPurged()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var bus = new CapturingBus();
        await using var sp = BuildProvider(bus, o => o.UseSqlite(connection));
        await RunOutboxFlowAsync(sp, bus);
    }

    private static async Task RunOutboxFlowAsync(ServiceProvider sp, CapturingBus bus)
    {

        using (var scope = sp.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await ctx.Database.EnsureCreatedAsync();

            var order = new Order { Id = Guid.NewGuid() };
            order.Place(total: 42m);
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync();
        }

        // AddMessagingOutbox must also wire host health. OutboxHealthContributor existed but nothing
        // registered it, so HostHealthAggregator — which the generated host registers — reported no outbox
        // at all. Asserted here, where the real registration path has run and the schema exists; testing
        // the contributor class on its own could never have caught a missing registration.
        var health = sp.GetServices<global::Pragmatic.ControlPlane.IHostHealthContributor>()
            .Should().ContainSingle(c => c.Name == "Messaging.Outbox").Subject;
        (await health.CheckAsync()).Status.Should().Be(global::Pragmatic.ControlPlane.ContributorHealthStatus.Healthy,
            "one pending message is far below the degraded threshold");

        // The event was captured into __OutboxMessages in the same transaction as the entity.
        using (var scope = sp.CreateScope())
        {
            var probe = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            (await probe.Set<OutboxMessage>().CountAsync()).Should().Be(1);
            (await probe.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt == null)).Should().Be(1);
        }

        // Delivery pump publishes to the transport and marks the row processed.
        var pump = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<OutboxDeliveryService>().Single();
        await pump.StartAsync(CancellationToken.None);

        // Wait for the row to be MARKED PROCESSED, not merely published. The pump publishes to the
        // transport first and marks the row afterwards, so waiting on bus.Published leaves a window
        // in which ProcessedAt is still null — StopAsync could land inside it and the assertion below
        // would read 0. (Re-delivering an unmarked row is correct at-least-once behaviour in
        // production; it is only the test that must not assert before the mark is durable.)
        await WaitUntilAsync(
            () =>
            {
                try
                {
                    using var waitScope = sp.CreateScope();
                    var db = waitScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                    return db.Set<OutboxMessage>().Count(m => m.ProcessedAt != null) == 1;
                }
                catch (DbException)
                {
                    // The pump holds the connection while it writes the mark; on the shared in-memory
                    // SQLite a concurrent probe surfaces as "database is locked" (SQLite error 5).
                    // A probe that could not read has not OBSERVED the condition — treat it as
                    // "not yet" and poll again. The assertion after the wait is not tolerant, so a
                    // genuinely broken database still fails the test.
                    return false;
                }
            },
            TimeSpan.FromSeconds(10));

        await pump.StopAsync(CancellationToken.None);

        bus.Published.Should().ContainSingle().Which.Should().BeOfType<OrderPlaced>()
            .Which.Total.Should().Be(42m);

        using (var scope = sp.CreateScope())
        {
            var probe = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            (await probe.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null)).Should().Be(1);
        }

        // Retention purge deletes the delivered row (retention zero → everything processed is eligible).
        using (var scope = sp.CreateScope())
        {
            var source = scope.ServiceProvider.GetServices<IOutboxSource>().Single();
            (await source.PurgeProcessedAsync(TimeSpan.Zero)).Should().Be(1);
        }

        using (var scope = sp.CreateScope())
        {
            var probe = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            (await probe.Set<OutboxMessage>().CountAsync()).Should().Be(0, "the delivered row was purged");
        }
    }

    /// <summary>
    ///     Polls until <paramref name="condition" /> holds, and THROWS if it never does. Returning
    ///     quietly on timeout let the caller run on and fail on a later assertion, reporting the
    ///     symptom ("expected 1, found 0") instead of the cause (the wait timed out).
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition was still false after {timeout.TotalSeconds:0.#}s.");
    }

    // --- Test doubles -------------------------------------------------------------------------

    private sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(e =>
            {
                e.HasKey(o => o.Id);
                e.Ignore(o => o.DomainEvents);
            });

            // Exactly what the SG-generated OnModelCreating emits for an [EnableOutbox] boundary.
            modelBuilder.AddMessagingOutbox();
        }
    }

    private sealed class Order : IHasDomainEvents
    {
        private readonly List<IDomainEvent> _events = [];
        public Guid Id { get; set; }
        public decimal Total { get; private set; }

        public void Place(decimal total)
        {
            Total = total;
            _events.Add(new OrderPlaced(Id, total, DateTimeOffset.UtcNow));
        }

        public IReadOnlyList<IDomainEvent> DomainEvents => _events;
        public void ClearDomainEvents() => _events.Clear();
    }

    private sealed record OrderPlaced(Guid OrderId, decimal Total, DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class OrderPlacedRegistry : IMessageTypeRegistry
    {
        // The interceptor serializes through PragmaticJsonOptions (camelCase), so match that here.
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        public object? Deserialize(string fullyQualifiedTypeName, string json)
            => fullyQualifiedTypeName == typeof(OrderPlaced).FullName
                ? JsonSerializer.Deserialize<OrderPlaced>(json, Options)
                : null;
    }

    private sealed class CapturingBus : IMessageBus
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync(object message, Type messageType, MessageContext context, CancellationToken ct = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task PublishAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task DispatchAsync(object message, MessageContext context, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SendAsync<T>(T message, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task SendAsync<T>(T message, MessageContext context, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
            where TRequest : notnull where TResponse : notnull => throw new NotSupportedException();
    }
}
