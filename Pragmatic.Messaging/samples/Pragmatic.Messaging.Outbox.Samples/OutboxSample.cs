using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Events;
using Pragmatic.Messaging;
using Pragmatic.Messaging.EFCore;
using Pragmatic.Messaging.Entities;
using Pragmatic.Messaging.Extensions;

namespace Pragmatic.Messaging.Outbox.Samples;

/// <summary>
///     The transactional outbox pattern: an aggregate's domain events are
///     captured by OutboxInterceptor during SaveChangesAsync and persisted as
///     OutboxMessage rows in the SAME transaction as the business data. A
///     background service (OutboxDeliveryService) then polls and republishes
///     them through IMessageBus, guaranteeing at-least-once delivery even if the
///     process crashes after commit but before publish.
///
///     Setup: SQLite in-memory (the interceptor needs real transactional
///     SaveChanges which EF Core InMemory does not provide), a custom DbContext
///     applying the outbox configuration + interceptor, a hand-rolled
///     IMessageTypeRegistry (in a real app the SG emits
///     PragmaticMessageTypeRegistry), and a probe handler that counts deliveries.
/// </summary>
public static class OutboxSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("--- Transactional outbox (SQLite in-memory) ---");

        var observer = new DeliveryObserver();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(b => b.ClearProviders().SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                // SQLite in-memory, with the connection kept open as a singleton so
                // schema + rows survive between DbContext instances.
                services.AddSingleton(_ =>
                {
                    var conn = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
                    conn.Open();
                    return conn;
                });

                services.AddDbContext<OrdersDbContext>((sp, options) =>
                {
                    options.UseSqlite(sp.GetRequiredService<Microsoft.Data.Sqlite.SqliteConnection>());
                    // OutboxInterceptor fires inside SavingChangesAsync so outbox rows
                    // are written in the same transaction as the aggregate's own
                    // writes — the whole point of the transactional-outbox pattern.
                    options.AddInterceptors(new OutboxInterceptor());
                });

                services.AddPragmaticMessaging(b => b.EnableOutbox(o =>
                {
                    o.PollingIntervalSeconds = 1;
                    o.BatchSize = 20;
                }));

                // Per-boundary outbox source — the SG does this for every DbContext
                // carrying [EnableOutbox]; here we register it by hand. The
                // IOutboxSource abstraction lives in Pragmatic.Messaging.Entities;
                // the EF Core implementation lives in Pragmatic.Messaging.EFCore.
                services.AddScoped<IOutboxSource>(sp =>
                    new EfCoreOutboxSource(
                        sp.GetRequiredService<OrdersDbContext>(),
                        boundaryName: "Orders",
                        sp.GetRequiredService<ILogger<EfCoreOutboxSource>>()));

                // The delivery service needs an IMessageTypeRegistry to know how to
                // materialize JSON payloads back into strongly-typed messages.
                services.AddSingleton<IMessageTypeRegistry, SampleTypeRegistry>();

                services.AddSingleton(observer);
                services.AddScoped<IMessageHandler<OrderPlaced>, OrderPlacedHandler>();

                services.AddHostedService<OutboxDeliveryService>();
            })
            .Build();

        await host.StartAsync();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Database.EnsureCreated();

            db.Orders.Add(Order.Place(Guid.NewGuid(), 49.90m));
            db.Orders.Add(Order.Place(Guid.NewGuid(), 120.00m));
            db.Orders.Add(Order.Place(Guid.NewGuid(), 7.50m));
            await db.SaveChangesAsync();

            var outboxCount = await db.Set<OutboxMessage>().CountAsync();
            Console.WriteLine($"  outbox rows after save    : {outboxCount} (interceptor captured events atomically)");
        }

        // The delivery service polls on PollingIntervalSeconds. Give it a generous
        // window so a cold start + first poll can drain all three messages.
        var delivered = await WaitForAsync(() => observer.Count >= 3, TimeSpan.FromSeconds(10));
        Console.WriteLine($"  handler deliveries seen   : {observer.Count} (expected 3)");
        Console.WriteLine($"  all delivered in time     : {delivered}");

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var pending = await db.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt == null);
            Console.WriteLine($"  pending rows after drain  : {pending} (outbox empties once delivered)");
        }

        await host.StopAsync();
        Console.WriteLine();
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(150);
        }
        return predicate();
    }

    // ------------------------------------------------------------------------
    // Domain model + infrastructure used by the outbox sample.
    // ------------------------------------------------------------------------

    public sealed class Order : IHasDomainEvents
    {
        private readonly List<IDomainEvent> _events = [];

        public Guid Id { get; private set; }
        public decimal Total { get; private set; }
        public DateTimeOffset PlacedAt { get; private set; }

        public IReadOnlyList<IDomainEvent> DomainEvents => _events;
        public void ClearDomainEvents() => _events.Clear();

        public static Order Place(Guid id, decimal total)
        {
            var order = new Order { Id = id, Total = total, PlacedAt = DateTimeOffset.UtcNow };
            order._events.Add(new OrderPlaced(id, total, order.PlacedAt));
            return order;
        }
    }

    public sealed record OrderPlaced(Guid OrderId, decimal Total, DateTimeOffset OccurredAt) : IDomainEvent;

    public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>(e =>
            {
                e.HasKey(o => o.Id);
                e.Ignore(o => o.DomainEvents);
            });
            modelBuilder.ApplyConfiguration(new OutboxEntityTypeConfiguration());
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            // SQLite can't ORDER BY DateTimeOffset natively; a long-ticks
            // representation keeps the column orderable without losing precision.
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }

    /// <summary>
    ///     Hand-rolled registry. A real application relies on the SG-generated
    ///     PragmaticMessageTypeRegistry, an AOT-safe switch expression covering
    ///     every message type discovered across the build.
    /// </summary>
    public sealed class SampleTypeRegistry : IMessageTypeRegistry
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public object? Deserialize(string fullyQualifiedTypeName, string json)
            => fullyQualifiedTypeName == typeof(OrderPlaced).FullName
                ? JsonSerializer.Deserialize<OrderPlaced>(json, Json)
                : null;
    }

    public sealed class DeliveryObserver
    {
        private int _count;
        public int Count => _count;
        public void Record() => Interlocked.Increment(ref _count);
        public ConcurrentBag<Guid> DeliveredIds { get; } = [];
    }

    public sealed class OrderPlacedHandler(DeliveryObserver observer) : IMessageHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced message, MessageContext context, CancellationToken ct)
        {
            observer.Record();
            observer.DeliveredIds.Add(message.OrderId);
            return Task.CompletedTask;
        }
    }
}
